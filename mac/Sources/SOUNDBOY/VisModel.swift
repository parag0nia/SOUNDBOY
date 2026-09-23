import Foundation
import SoundboyCore

enum VisMode: Int, Codable, CaseIterable {
    case spectrum, oscilloscope, off
}

/// Spectrum bars with falloff + peak caps, and an oscilloscope snapshot (same math as Windows).
/// Separate from PlayerModel so only the visualizer redraws at 60 fps.
final class VisModel: ObservableObject {
    static let barCount = 44
    private let n = 2048

    @Published private(set) var bars = [Float](repeating: 0, count: VisModel.barCount)
    @Published private(set) var peaks = [Float](repeating: 0, count: VisModel.barCount)
    @Published private(set) var scope = [Float](repeating: 0, count: 392)

    private var buf: [Float]
    private var re: [Double]
    private var im: [Double]
    private let window: [Double]
    private var hold = [Float](repeating: 0, count: VisModel.barCount)

    init() {
        buf = [Float](repeating: 0, count: n)
        re = [Double](repeating: 0, count: n)
        im = [Double](repeating: 0, count: n)
        window = (0..<n).map { 0.5 * (1 - cos(2 * Double.pi * Double($0) / Double(n - 1))) }
    }

    /// Demo state for snapshots.
    func setDemo() {
        var demo = [Float](repeating: 0, count: VisModel.barCount)
        for i in 0..<VisModel.barCount {
            let wave: Double = abs(sin(Double(i) * 0.37))
            let fade: Double = 1 - Double(i) / 60
            demo[i] = Float(0.25 + 0.6 * wave * fade)
        }
        bars = demo
        peaks = demo.map { min(1, $0 + 0.08) }
    }

    func update(tap: SampleTap, active: Bool, dt: Float) {
        if active { tap.read(into: &buf) } else { for i in 0..<n { buf[i] = 0 } }
        var newScope = scope
        for i in 0..<newScope.count { newScope[i] = buf[n - newScope.count + i] }

        for i in 0..<n { re[i] = Double(buf[i]) * window[i]; im[i] = 0 }
        DSP.fft(&re, &im)

        let sr = max(8000, tap.sampleRate)
        let fMin = 40.0, fMax = min(16000, sr / 2)
        let binHz = sr / Double(n)
        var newBars = bars, newPeaks = peaks
        let nb = VisModel.barCount
        for b in 0..<nb {
            let f0 = fMin * pow(fMax / fMin, Double(b) / Double(nb))
            let f1 = fMin * pow(fMax / fMin, Double(b + 1) / Double(nb))
            let k0 = min(max(Int(f0 / binHz), 1), n / 2 - 1)
            let k1 = min(max(Int((f1 / binHz).rounded(.up)), k0 + 1), n / 2)
            var mx = 0.0
            for k in k0..<k1 { mx = max(mx, re[k] * re[k] + im[k] * im[k]) }
            let mag = mx.squareRoot() / (Double(n) / 4)
            var db = 20 * log10(mag + 1e-9)
            db += 2.4 * log2((f0 * f1).squareRoot() / 1000) // tilt so treble isn't always flat
            let level = Float(min(max((db + 64) / 60, 0), 1))

            if level > newBars[b] { newBars[b] += (level - newBars[b]) * 0.8 }
            else { newBars[b] = max(level, newBars[b] - dt * 1.7) }

            if newBars[b] >= newPeaks[b] { newPeaks[b] = newBars[b]; hold[b] = 0.35 }
            else if hold[b] > 0 { hold[b] -= dt }
            else { newPeaks[b] = max(newBars[b], newPeaks[b] - dt * 0.9) }
        }
        bars = newBars
        peaks = newPeaks
        scope = newScope
    }
}
