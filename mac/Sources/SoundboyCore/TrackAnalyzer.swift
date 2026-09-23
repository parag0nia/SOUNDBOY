import AVFoundation
import Foundation

/// Result of musical analysis. Any field may be nil when it can't be determined reliably.
public struct AnalysisResult: Codable, Equatable, Sendable {
    public var bpm: Double?
    public var key: String?
    public var camelot: String?
    public init(bpm: Double? = nil, key: String? = nil, camelot: String? = nil) {
        self.bpm = bpm; self.key = key; self.camelot = camelot
    }
}

/// Offline tempo and key detection (same algorithm as the Windows version):
///  • BPM — spectral-flux onset envelope → autocorrelation over 60–200 BPM with a log-normal prior
///    centered on 120 BPM, refined with a multi-beat comb.
///  • Key — chromagram (65 Hz–2 kHz) correlated with the Krumhansl–Schmuckler major/minor profiles.
public enum TrackAnalyzer {
    static let targetRate = 11025.0
    static let maxSeconds = 150
    static let onsetFft = 1024, onsetHop = 128
    static let chromaFft = 8192, chromaHop = 2048

    static let majorProfile: [Double] = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
    static let minorProfile: [Double] = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]
    static let majorNames = ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]
    static let minorNames = ["Cm", "C#m", "Dm", "Ebm", "Em", "Fm", "F#m", "Gm", "G#m", "Am", "Bbm", "Bm"]
    static let camelotMajor = [8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1]
    static let camelotMinor = [5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10]

    public static func analyze(url: URL, isCancelled: () -> Bool = { false }) throws -> AnalysisResult {
        let (x, rate) = try decodeMono(url, isCancelled)
        let seconds = Double(x.count) / Double(rate)
        if seconds < 3 { return AnalysisResult() }
        let bpm = seconds >= 8 ? detectBpm(x, rate) : nil
        let (key, camelot) = detectKey(x, rate)
        return AnalysisResult(bpm: bpm, key: key, camelot: camelot)
    }

    // MARK: decoding

    static func decodeMono(_ url: URL, _ isCancelled: () -> Bool) throws -> ([Float], Int) {
        let file = try AVAudioFile(forReading: url)
        let format = file.processingFormat // deinterleaved Float32
        let channels = Int(format.channelCount)
        let factor = max(1, Int((format.sampleRate / targetRate).rounded()))
        let rate = Int(format.sampleRate) / factor
        let maxOut = maxSeconds * rate
        guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 65536) else { return ([], rate) }

        var out: [Float] = []
        out.reserveCapacity(min(maxOut, rate * 60))
        var acc: Float = 0
        var accN = 0
        while out.count < maxOut && file.framePosition < file.length {
            if isCancelled() { throw CancellationError() }
            try file.read(into: buffer)
            let n = Int(buffer.frameLength)
            guard n > 0, let data = buffer.floatChannelData else { break }
            for f in 0..<n {
                var m: Float = 0
                for c in 0..<channels { m += data[c][f] }
                acc += m / Float(channels)
                accN += 1
                if accN == factor { // boxcar low-pass + decimate
                    out.append(acc / Float(factor))
                    acc = 0
                    accN = 0
                    if out.count >= maxOut { break }
                }
            }
        }
        return (out, rate)
    }

    // MARK: tempo

    static func detectBpm(_ x: [Float], _ rate: Int) -> Double? {
        let fps = Double(rate) / Double(onsetHop)
        let window = hann(onsetFft)
        var re = [Double](repeating: 0, count: onsetFft)
        var im = [Double](repeating: 0, count: onsetFft)
        let bins = onsetFft / 2
        var prev = [Double](repeating: 0, count: bins)
        let frames = (x.count - onsetFft) / onsetHop
        if Double(frames) < fps * 6 { return nil }
        var onset = [Double](repeating: 0, count: frames)

        for f in 0..<frames {
            let o = f * onsetHop
            for i in 0..<onsetFft { re[i] = Double(x[o + i]) * window[i]; im[i] = 0 }
            DSP.fft(&re, &im)
            var flux = 0.0
            for k in 1..<bins {
                let mag = log(1 + 100 * (re[k] * re[k] + im[k] * im[k]).squareRoot())
                let d = mag - prev[k]
                if d > 0 { flux += d }
                prev[k] = mag
            }
            onset[f] = flux
        }

        // Remove the slowly varying loudness trend so the autocorrelation sees beats, not dynamics.
        let w = Int(fps * 0.5)
        var detr = [Double](repeating: 0, count: frames)
        var run = 0.0
        for i in 0..<frames {
            run += onset[i]
            if i >= w { run -= onset[i - w] }
            let mean = run / Double(min(i + 1, w))
            detr[i] = max(0, onset[i] - mean)
        }

        func ac(_ lag: Double) -> Double {
            let l0 = Int(lag.rounded(.down))
            let t = lag - Double(l0)
            var s0 = 0.0, s1 = 0.0
            var i = 0
            while i + l0 + 1 < frames {
                s0 += detr[i] * detr[i + l0]
                s1 += detr[i] * detr[i + l0 + 1]
                i += 1
            }
            return (s0 * (1 - t) + s1 * t) / Double(max(1, frames - l0))
        }

        let energy = ac(0)
        if energy <= 1e-9 { return nil }

        var cache: [Int: Double] = [:]
        func acInt(_ l: Int) -> Double {
            if let v = cache[l] { return v }
            let v = ac(Double(l))
            cache[l] = v
            return v
        }

        // Coarse search on integer lags with a tempo prior centered on 120 BPM (1 octave std).
        let minLag = Int((60 * fps / 200).rounded(.down)), maxLag = Int((60 * fps / 60).rounded(.up))
        var bestScore = -Double.infinity, bestLag = minLag
        for lag in minLag...maxLag {
            let bpm = 60 * fps / Double(lag)
            let prior = exp(-0.5 * pow(log2(bpm / 120), 2))
            let score = (acInt(lag) + 0.5 * acInt(lag * 2) + 0.25 * acInt(lag * 4)) * prior
            if score > bestScore { bestScore = score; bestLag = lag }
        }
        if acInt(bestLag) / energy < 0.02 { return nil } // no periodicity at all

        // Fine search around the winner: comb over several beats for sub-frame accuracy.
        let bestBpm = 60 * fps / Double(bestLag)
        var fineScore = -Double.infinity, fineBpm = bestBpm
        var b = bestBpm - 3
        while b <= bestBpm + 3 {
            let lag = 60 * fps / b
            var s = 0.0
            for k in 1...4 { s += ac(lag * Double(k)) / Double(k) }
            if s > fineScore { fineScore = s; fineBpm = b }
            b += 0.05
        }
        return (fineBpm * 10).rounded() / 10
    }

    // MARK: key

    static func detectKey(_ x: [Float], _ rate: Int) -> (String?, String?) {
        let window = hann(chromaFft)
        var re = [Double](repeating: 0, count: chromaFft)
        var im = [Double](repeating: 0, count: chromaFft)
        var chroma = [Double](repeating: 0, count: 12)
        let kMin = Int((65.0 * Double(chromaFft) / Double(rate)).rounded(.up))
        let kMax = Int(2000.0 * Double(chromaFft) / Double(rate))
        guard kMax > kMin else { return (nil, nil) }
        var pcOfBin = [Int](repeating: 0, count: kMax + 1)
        for k in kMin...kMax {
            let f = Double(k) * Double(rate) / Double(chromaFft)
            let midi = Int((69 + 12 * log2(f / 440)).rounded())
            pcOfBin[k] = ((midi % 12) + 12) % 12
        }

        let frames = max(0, (x.count - chromaFft) / chromaHop + 1)
        if frames < 2 { return (nil, nil) }
        for f in 0..<frames {
            let o = f * chromaHop
            for i in 0..<chromaFft { re[i] = Double(x[o + i]) * window[i]; im[i] = 0 }
            DSP.fft(&re, &im)
            var frame = [Double](repeating: 0, count: 12)
            var total = 0.0
            for k in kMin...kMax {
                let mag = (re[k] * re[k] + im[k] * im[k]).squareRoot()
                frame[pcOfBin[k]] += mag
                total += mag
            }
            if total <= 1e-9 { continue }
            for p in 0..<12 { chroma[p] += frame[p] / total } // per-frame normalization: loudness-independent
        }
        if chroma.reduce(0, +) <= 1e-9 { return (nil, nil) }

        var best = -Double.infinity, bestRoot = 0, bestMinor = false
        for root in 0..<12 {
            for minor in [false, true] {
                let prof = minor ? minorProfile : majorProfile
                let r = pearson(chroma) { prof[(($0 - root) % 12 + 12) % 12] }
                if r > best { best = r; bestRoot = root; bestMinor = minor }
            }
        }
        if best < 0.3 { return (nil, nil) } // atonal / noise / speech
        return bestMinor
            ? (minorNames[bestRoot], "\(camelotMinor[bestRoot])A")
            : (majorNames[bestRoot], "\(camelotMajor[bestRoot])B")
    }

    static func pearson(_ a: [Double], _ b: (Int) -> Double) -> Double {
        let ma = a.reduce(0, +) / 12
        var mb = 0.0
        for i in 0..<12 { mb += b(i) }
        mb /= 12
        var num = 0.0, da = 0.0, db = 0.0
        for i in 0..<12 {
            let x = a[i] - ma, y = b(i) - mb
            num += x * y
            da += x * x
            db += y * y
        }
        return num / (da * db + 1e-12).squareRoot()
    }

    static func hann(_ n: Int) -> [Double] {
        (0..<n).map { 0.5 * (1 - cos(2 * Double.pi * Double($0) / Double(n - 1))) }
    }
}

public enum DSP {
    /// In-place iterative radix-2 complex FFT. Length must be a power of two.
    public static func fft(_ re: inout [Double], _ im: inout [Double]) {
        let n = re.count
        re.withUnsafeMutableBufferPointer { r in
            im.withUnsafeMutableBufferPointer { i in
                var j = 0
                for a in 1..<n {
                    var bit = n >> 1
                    while j & bit != 0 { j ^= bit; bit >>= 1 }
                    j ^= bit
                    if a < j { r.swapAt(a, j); i.swapAt(a, j) }
                }
                var len = 2
                while len <= n {
                    let ang = -2 * Double.pi / Double(len)
                    let wr = cos(ang), wi = sin(ang)
                    let half = len / 2
                    var s = 0
                    while s < n {
                        var cr = 1.0, ci = 0.0
                        for k in 0..<half {
                            let a = s + k, b = a + half
                            let tr = r[b] * cr - i[b] * ci
                            let ti = r[b] * ci + i[b] * cr
                            r[b] = r[a] - tr; i[b] = i[a] - ti
                            r[a] += tr; i[a] += ti
                            let ncr = cr * wr - ci * wi
                            ci = cr * wi + ci * wr
                            cr = ncr
                        }
                        s += len
                    }
                    len <<= 1
                }
            }
        }
    }
}
