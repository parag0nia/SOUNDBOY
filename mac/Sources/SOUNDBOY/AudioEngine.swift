import AVFoundation
import SoundboyCore

/// Ring buffer of recent mono samples for the visualizer, written from the audio render thread.
final class SampleTap: @unchecked Sendable {
    private let size = 1 << 15
    private var ring: [Float]
    private var written = 0
    private let lock = NSLock()
    private(set) var sampleRate: Double = 44100

    init() { ring = [Float](repeating: 0, count: size) }

    func write(_ buffer: AVAudioPCMBuffer) {
        guard let data = buffer.floatChannelData else { return }
        let n = Int(buffer.frameLength), ch = Int(buffer.format.channelCount)
        lock.lock(); defer { lock.unlock() }
        sampleRate = buffer.format.sampleRate
        for f in 0..<n {
            var m: Float = 0
            for c in 0..<ch { m += data[c][f] }
            ring[(written + f) & (size - 1)] = m / Float(ch)
        }
        written += n
    }

    func read(into dest: inout [Float]) {
        lock.lock(); defer { lock.unlock() }
        let start = written - dest.count
        for i in 0..<dest.count {
            let idx = start + i
            dest[i] = idx < 0 || idx < written - size ? 0 : ring[idx & (size - 1)]
        }
    }

    func clear() {
        lock.lock(); defer { lock.unlock() }
        for i in 0..<size { ring[i] = 0 }
        written = 0
    }
}

/// Playback: player → mixer (balance) → 10-band EQ + preamp → main mixer (volume) → output.
final class AudioEngine {
    enum State { case stopped, playing, paused }

    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private let submix = AVAudioMixerNode()
    let eq = AVAudioUnitEQ(numberOfBands: Equalizer.bandCount)
    let tap = SampleTap()

    private var graphReady = false
    private(set) var file: AVAudioFile?
    private(set) var state: State = .stopped
    private var startFrame: AVAudioFramePosition = 0
    private var generation = 0
    private var volume: Float = 0.8

    /// Called on the main thread when a track plays to the end.
    var onTrackEnded: (() -> Void)?

    init() {
        for (i, band) in eq.bands.enumerated() {
            band.filterType = .parametric
            band.frequency = Equalizer.frequencies[i]
            band.bandwidth = 1.0
            band.gain = 0
            band.bypass = false
        }
        NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main) { [weak self] _ in
            self?.handleConfigurationChange()
        }
    }

    /// Built lazily so the app never touches the audio hardware until something is played.
    private func ensureGraph(format: AVAudioFormat) {
        if !graphReady {
            engine.attach(player)
            engine.attach(submix)
            engine.attach(eq)
            engine.connect(submix, to: eq, format: nil)
            engine.connect(eq, to: engine.mainMixerNode, format: nil)
            eq.installTap(onBus: 0, bufferSize: 1024, format: nil, block: Self.tapBlock(tap))
            engine.mainMixerNode.outputVolume = volume * volume
            graphReady = true
        }
        engine.disconnectNodeOutput(player)
        engine.connect(player, to: submix, format: format)
    }

    private static func tapBlock(_ tap: SampleTap) -> AVAudioNodeTapBlock {
        { buffer, _ in tap.write(buffer) }
    }

    var duration: TimeInterval {
        guard let f = file else { return 0 }
        return Double(f.length) / f.processingFormat.sampleRate
    }

    var position: TimeInterval {
        guard let f = file else { return 0 }
        let sr = f.processingFormat.sampleRate
        var frame = startFrame
        if state != .stopped, let nodeTime = player.lastRenderTime, nodeTime.isSampleTimeValid,
           let playerTime = player.playerTime(forNodeTime: nodeTime) {
            frame += playerTime.sampleTime
        }
        return min(max(0, Double(frame) / sr), duration)
    }

    var sourceSampleRate: Double { file?.fileFormat.sampleRate ?? 0 }
    var sourceChannels: Int { Int(file?.fileFormat.channelCount ?? 0) }

    func load(_ url: URL) throws {
        stop()
        let f = try AVAudioFile(forReading: url)
        file = f
        ensureGraph(format: f.processingFormat)
        startFrame = 0
    }

    func unload() {
        stop()
        file = nil
    }

    func play() throws {
        guard file != nil else { return }
        if state == .playing { return }
        if !engine.isRunning { try engine.start() }
        if state == .stopped { schedule(from: startFrame) }
        player.play()
        state = .playing
    }

    func pause() {
        guard state == .playing else { return }
        player.pause()
        state = .paused
    }

    func stop() {
        generation += 1
        if graphReady { player.stop() }
        state = .stopped
        startFrame = 0
        tap.clear()
    }

    func seek(to time: TimeInterval) {
        guard let f = file else { return }
        let frame = AVAudioFramePosition(min(max(0, time), duration) * f.processingFormat.sampleRate)
        let previous = state
        generation += 1
        player.stop()
        startFrame = frame
        guard previous != .stopped else { return }
        schedule(from: frame)
        if previous == .playing { player.play() }
        state = previous
    }

    private func schedule(from frame: AVAudioFramePosition) {
        guard let f = file else { return }
        generation += 1
        let gen = generation
        startFrame = frame
        let count = AVAudioFrameCount(max(0, f.length - frame))
        guard count > 0 else {
            DispatchQueue.main.async { [weak self] in self?.segmentFinished(gen) }
            return
        }
        player.scheduleSegment(f, startingFrame: frame, frameCount: count, at: nil,
                               completionCallbackType: .dataPlayedBack,
                               completionHandler: Self.completion(self, gen))
    }

    private static func completion(_ engine: AudioEngine, _ gen: Int) -> AVAudioPlayerNodeCompletionHandler {
        { [weak engine] _ in DispatchQueue.main.async { engine?.segmentFinished(gen) } }
    }

    private func segmentFinished(_ gen: Int) {
        // Stale callbacks (from stop/seek, which also fire completions) are ignored.
        guard gen == generation, state == .playing else { return }
        generation += 1
        player.stop()
        state = .stopped
        startFrame = 0
        onTrackEnded?()
    }

    private func handleConfigurationChange() {
        // Output device changed (headphones, AirPods, ...): the engine stopped; restart where we were.
        guard graphReady, file != nil else { return }
        let resumeAt = position
        let wasPlaying = state == .playing
        generation += 1
        player.stop()
        if wasPlaying {
            state = .stopped
            startFrame = AVAudioFramePosition(resumeAt * (file?.processingFormat.sampleRate ?? 44100))
            try? play()
        }
    }

    // MARK: mixing

    func setVolume(_ v: Double) {
        volume = Float(min(max(v, 0), 1))
        engine.mainMixerNode.outputVolume = volume * volume // perceptual taper
    }

    func setBalance(_ b: Double) { player.pan = Float(min(max(b, -1), 1)) }

    func setEq(enabled: Bool, preamp: Float, bands: [Float]) {
        eq.bypass = !enabled
        eq.globalGain = preamp
        for (i, band) in eq.bands.enumerated() where i < bands.count { band.gain = bands[i] }
    }
}
