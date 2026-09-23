import AppKit
import SwiftUI
import AVFoundation
import SoundboyCore
import UniformTypeIdentifiers

/// Thread-safe slots for results produced by parallel workers.
final class ResultsBox: @unchecked Sendable {
    private var items: [AnalysisResult?]
    private var done = 0
    private let lock = NSLock()

    init(count: Int) { items = [AnalysisResult?](repeating: nil, count: count) }

    /// Stores a result and returns how many are done.
    func set(_ i: Int, _ r: AnalysisResult) -> Int {
        lock.lock(); defer { lock.unlock() }
        items[i] = r
        done += 1
        return done
    }

    var results: [AnalysisResult?] {
        lock.lock(); defer { lock.unlock() }
        return items
    }
}

struct RekordboxHelpInfo: Identifiable {
    let id = UUID()
    let xmlURL: URL
    let result: RekordboxExport.Result?
    let configuredInRekordbox: Bool
    let sidebarEnabled: Bool
}

/// App state and commands (the Mac counterpart of the Windows MainForm controller). Main thread only.
final class PlayerModel: ObservableObject {
    static let shared = PlayerModel()
    static let version = (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "2.1.0"

    @Published var tracks: [Track] = []
    @Published var selection = Set<Track.ID>()
    @Published private(set) var current: Track?
    @Published private(set) var state: AudioEngine.State = .stopped
    @Published private(set) var position: TimeInterval = 0
    @Published private(set) var duration: TimeInterval = 0
    @Published var settings = AppSettings.load()
    @Published private(set) var transient: String?
    @Published private(set) var artwork: NSImage?
    @Published var rekordboxHelp: RekordboxHelpInfo?
    @Published var fileInfo: Track?
    @Published var search = ""
    @Published private(set) var rekordboxBusy = false

    let engine = AudioEngine()
    let vis = VisModel()
    let analysisCache = AnalysisCache()
    let nowPlaying = NowPlaying()
    private(set) var loadedTrack: Track?
    var stopAfterCurrent = false

    private var shuffleOrder: [Track]?
    private var timer: Timer?
    private var lastTick = Date()
    private var transientUntil = Date.distantPast
    private var analysisToken = 0
    private var failStreak = 0
    private var volumeBeforeMute = 0.8
    private var isDemo = false

    private init() {
        engine.onTrackEnded = { [weak self] in self?.trackEnded() }
        engine.setVolume(settings.volume)
        engine.setBalance(settings.balance)
        applyEqToEngine()
        nowPlaying.install(self)
        loadAutosave()
        ensureSample()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(timer!, forMode: .common)
    }

    // MARK: - main loop

    private func tick() {
        let now = Date()
        let dt = Float(min(0.1, now.timeIntervalSince(lastTick)))
        lastTick = now
        if !isDemo && !settings.compact && settings.vis != .off {
            vis.update(tap: engine.tap, active: engine.state == .playing, dt: dt)
        }
        if engine.state != state { state = engine.state; nowPlaying.update(self) }
        let p = engine.position
        if abs(p - position) > 0.05 || (p == 0 && position != 0) { position = p }
        if transient != nil, now > transientUntil { transient = nil }
    }

    func showTransient(_ text: String, seconds: Double = 1.6) {
        transient = text
        transientUntil = Date().addingTimeInterval(seconds)
    }

    // MARK: - now playing text

    var nowPlayingTitle: String { current?.displayTitleOnly ?? "SOUNDBOY" }

    var nowPlayingSubtitle: String {
        guard let t = current else { return tracks.isEmpty ? "Drop music here or press ⌘O to open files" : "Press play to start" }
        var parts = [t.artist?.isEmpty == false ? t.artist! : "Unknown artist"]
        if let a = t.album, !a.isEmpty { parts.append(a) }
        if t.year > 0 { parts.append(String(t.year)) }
        return parts.joined(separator: " · ")
    }

    /// Format chips, then the musical ones (BPM, key) which are drawn in the accent color.
    var chips: [(text: String, musical: Bool)] {
        guard let t = loadedTrack, engine.file != nil || isDemo else { return [] }
        var out: [(text: String, musical: Bool)] = [(t.fileExtension.uppercased(), false)]
        if t.bitrate > 0 { out.append(("\(t.bitrate) kbps", false)) }
        let sr = t.sampleRate > 0 ? Double(t.sampleRate) : engine.sourceSampleRate
        if sr > 0 { out.append((String(format: sr.truncatingRemainder(dividingBy: 1000) == 0 ? "%.0f kHz" : "%.1f kHz", sr / 1000), false)) }
        let ch = t.channels > 0 ? t.channels : engine.sourceChannels
        if ch > 0 { out.append((ch == 1 ? "MONO" : ch == 2 ? "STEREO" : "\(ch) CH", false)) }
        if t.analyzing && t.displayBpm == nil { out.append(("··· BPM", true)) }
        else if let bpm = t.displayBpm { out.append(("\(Int(bpm.rounded())) BPM", true)) }
        if let k = t.key { out.append(("\(k) · \(t.camelot ?? "")", true)) }
        else if t.analyzing { out.append(("KEY ···", true)) }
        return out
    }

    var totalDuration: TimeInterval { tracks.reduce(0) { $0 + ($1.duration ?? 0) } }
    var currentIndex: Int? { current.flatMap { c in tracks.firstIndex { $0 === c } } }

    // MARK: - playback

    func playTrack(_ t: Track, start: Bool = true) {
        current = t
        loadedTrack = nil
        do {
            try engine.load(t.url)
            loadedTrack = t
            duration = engine.duration
            if duration > 0, abs((t.duration ?? 0) - duration) > 1 { t.duration = duration }
            if start { try engine.play() }
            failStreak = 0
            t.missing = false
            state = engine.state
            if !t.infoLoaded { Task { @MainActor in await Metadata.load(t); self.objectWillChange.send(); self.nowPlaying.update(self) } }
            loadArtwork(t)
            startAnalysis(t)
        } catch {
            engine.unload()
            t.missing = !FileManager.default.fileExists(atPath: t.url.path)
            showTransient("Can't play \(t.fileNameTitle): \(error.localizedDescription)", seconds: 4)
            if start, failStreak + 1 < tracks.count, let next = nextTrack(wrap: settings.repeatMode != .off), next !== t {
                failStreak += 1
                DispatchQueue.main.async { self.playTrack(next) }
            }
        }
        position = 0
        nowPlaying.update(self)
        objectWillChange.send()
    }

    func playPressed() {
        switch engine.state {
        case .paused: resume()
        case .playing: engine.seek(to: 0)
        case .stopped:
            let selected = tracks.first { selection.contains($0.id) }
            guard let t = current ?? selected ?? tracks.first else { openFilesPanel(replace: true); return }
            if t === loadedTrack, engine.file != nil { resume() } else { playTrack(t) }
        }
        state = engine.state
    }

    private func resume() {
        do { try engine.play() } catch { showTransient("Audio output unavailable: \(error.localizedDescription)", seconds: 4) }
        state = engine.state
        nowPlaying.update(self)
    }

    func pause() { engine.pause(); state = engine.state; nowPlaying.update(self) }

    func playPause() {
        if engine.state == .playing { pause() } else { playPressed() }
    }

    func stop() {
        engine.stop()
        state = engine.state
        position = 0
        nowPlaying.update(self)
    }

    func next() {
        guard let n = nextTrack(wrap: true) else { return }
        playTrack(n, start: engine.state != .stopped)
    }

    func previous() {
        guard let p = previousTrack(wrap: true) else { return }
        playTrack(p, start: engine.state != .stopped)
    }

    private func trackEnded() {
        state = engine.state
        if stopAfterCurrent { stopAfterCurrent = false; nowPlaying.update(self); return }
        if settings.repeatMode == .one, let c = current { playTrack(c); return }
        if let n = nextTrack(wrap: settings.repeatMode == .all) { playTrack(n) } else { nowPlaying.update(self) }
    }

    func seek(fraction: Double) {
        guard engine.file != nil, engine.state != .stopped else { return }
        engine.seek(to: engine.duration * fraction)
        position = engine.position
        nowPlaying.update(self)
    }

    func seek(by seconds: Double) {
        guard engine.file != nil, engine.state != .stopped else { return }
        engine.seek(to: engine.position + seconds)
        position = engine.position
        nowPlaying.update(self)
    }

    func seek(to time: TimeInterval) {
        guard engine.file != nil else { return }
        engine.seek(to: time)
        position = engine.position
        nowPlaying.update(self)
    }

    // MARK: - shuffle / order

    private func shuffled() -> [Track] {
        if let o = shuffleOrder, o.count == tracks.count { return o }
        var o = tracks.shuffled()
        if let c = current, let i = o.firstIndex(where: { $0 === c }) { o.remove(at: i); o.insert(c, at: 0) }
        shuffleOrder = o
        return o
    }

    func nextTrack(wrap: Bool) -> Track? {
        guard !tracks.isEmpty else { return nil }
        if !settings.shuffle {
            let i = currentIndex ?? -1
            if i + 1 < tracks.count { return tracks[i + 1] }
            return wrap ? tracks[0] : nil
        }
        let o = shuffled()
        let k = current.flatMap { c in o.firstIndex { $0 === c } } ?? -1
        if k + 1 < o.count { return o[k + 1] }
        guard wrap else { return nil }
        shuffleOrder = nil
        let o2 = shuffled()
        return o2.count > 1 ? o2[1] : o2[0]
    }

    func previousTrack(wrap: Bool) -> Track? {
        guard !tracks.isEmpty else { return nil }
        if !settings.shuffle {
            guard let i = currentIndex else { return tracks[0] }
            if i > 0 { return tracks[i - 1] }
            return wrap ? tracks.last : tracks[0]
        }
        let o = shuffled()
        let k = current.flatMap { c in o.firstIndex { $0 === c } } ?? 0
        return k > 0 ? o[k - 1] : (wrap ? o.last : o[0])
    }

    // MARK: - volume / balance / toggles

    func setVolume(_ v: Double) {
        settings.volume = min(max(v, 0), 1)
        engine.setVolume(settings.volume)
        saveSettingsSoon()
    }

    func volume(by delta: Double) {
        setVolume(settings.volume + delta)
        showTransient("Volume \(Int(settings.volume * 100))%")
    }

    func toggleMute() {
        if settings.volume > 0.001 { volumeBeforeMute = settings.volume; setVolume(0); showTransient("Muted") }
        else { setVolume(volumeBeforeMute > 0.01 ? volumeBeforeMute : 0.8); showTransient("Volume \(Int(settings.volume * 100))%") }
    }

    func setBalance(_ b: Double) {
        settings.balance = abs(b) < 0.07 ? 0 : b
        engine.setBalance(settings.balance)
        saveSettingsSoon()
    }

    func toggleShuffle() {
        settings.shuffle.toggle()
        shuffleOrder = nil
        showTransient(settings.shuffle ? "Shuffle on" : "Shuffle off")
        saveSettingsSoon()
    }

    func cycleRepeat() {
        settings.repeatMode = RepeatMode(rawValue: (settings.repeatMode.rawValue + 1) % 3) ?? .off
        showTransient(["Repeat off", "Repeat: playlist", "Repeat: current track"][settings.repeatMode.rawValue])
        saveSettingsSoon()
    }

    func cycleVis() {
        settings.vis = VisMode(rawValue: (settings.vis.rawValue + 1) % 3) ?? .spectrum
        saveSettingsSoon()
    }

    func toggle(_ keyPath: WritableKeyPath<AppSettings, Bool>) {
        settings[keyPath: keyPath].toggle()
        if keyPath == \AppSettings.alwaysOnTop { applyWindowLevel() }
        saveSettingsSoon()
    }

    func applyWindowLevel() {
        for w in NSApp.windows where w.isVisible && w.identifier?.rawValue.contains("main") ?? true {
            w.level = settings.alwaysOnTop ? .floating : .normal
        }
    }

    // MARK: - equalizer

    func applyEqToEngine() {
        engine.setEq(enabled: settings.eqEnabled, preamp: settings.eqPreamp, bands: settings.eqBands)
    }

    func setBand(_ i: Int, _ db: Float) {
        settings.eqBands[i] = (db * 10).rounded() / 10
        applyEqToEngine()
        showTransient("EQ \(Equalizer.labels[i])Hz: \(String(format: "%+.1f", settings.eqBands[i])) dB")
        saveSettingsSoon()
    }

    func setPreamp(_ db: Float) {
        settings.eqPreamp = (db * 10).rounded() / 10
        applyEqToEngine()
        showTransient("EQ preamp: \(String(format: "%+.1f", settings.eqPreamp)) dB")
        saveSettingsSoon()
    }

    func applyEq(_ bands: [Float], preamp: Float) {
        settings.eqBands = bands.map { min(max($0, -12), 12) }
        settings.eqPreamp = min(max(preamp, -12), 12)
        applyEqToEngine()
        saveSettingsSoon()
    }

    func toggleEqEnabled() {
        settings.eqEnabled.toggle()
        applyEqToEngine()
        showTransient(settings.eqEnabled ? "Equalizer on" : "Equalizer off")
        saveSettingsSoon()
    }

    func savePresetPrompt() {
        let alert = NSAlert()
        alert.messageText = "Save EQ preset"
        alert.informativeText = "Preset name:"
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 240, height: 24))
        alert.accessoryView = field
        alert.addButton(withTitle: "Save")
        alert.addButton(withTitle: "Cancel")
        alert.window.initialFirstResponder = field
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        let name = field.stringValue.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        settings.userPresets[name] = settings.eqBands + [settings.eqPreamp]
        showTransient("Preset saved: \(name)")
        saveSettingsSoon()
    }

    func importEqf() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "eqf") ?? .data]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let list = try EqPresets.readEqf(url)
            for p in list { settings.userPresets[p.name] = p.bands + [p.preamp] }
            if list.count == 1 { applyEq(list[0].bands, preamp: list[0].preamp) }
            showTransient("Imported \(list.count) preset(s)", seconds: 2.5)
            saveSettingsSoon()
        } catch { alert("Import failed", error.localizedDescription) }
    }

    func exportEqf() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "soundboy.eqf"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let presets = settings.userPresets.sorted { $0.key < $1.key }.map {
            EqPreset(name: $0.key, bands: Array($0.value.prefix(10)), preamp: $0.value.count > 10 ? $0.value[10] : 0)
        }
        do { try EqPresets.writeEqf(presets, to: url) } catch { alert("Export failed", error.localizedDescription) }
    }

    // MARK: - opening files

    func open(_ urls: [URL], play: Bool, replace: Bool, at index: Int? = nil) {
        let new = MediaFiles.expand(urls)
        guard !new.isEmpty else {
            if !urls.isEmpty { showTransient("No playable files (macOS plays MP3, AAC/M4A, WAV, AIFF, FLAC, ALAC)", seconds: 4) }
            return
        }
        if replace { tracks.removeAll(); selection.removeAll() }
        let i = min(max(index ?? tracks.count, 0), tracks.count)
        tracks.insert(contentsOf: new, at: i)
        shuffleOrder = nil
        loadMetadata(new)
        if play { playTrack(new[0]) }
    }

    func openFilesPanel(replace: Bool) {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = true
        panel.allowedContentTypes = [.audio, UTType(filenameExtension: "m3u") ?? .data, UTType(filenameExtension: "m3u8") ?? .data,
                                     UTType(filenameExtension: "pls") ?? .data]
        panel.prompt = replace ? "Play" : "Add"
        guard panel.runModal() == .OK else { return }
        open(panel.urls, play: replace, replace: replace)
    }

    private func loadMetadata(_ list: [Track]) {
        Task { @MainActor in
            var n = 0
            for t in list where !t.infoLoaded {
                await Metadata.load(t)
                n += 1
                if n % 25 == 0 { self.objectWillChange.send() }
            }
            self.objectWillChange.send()
        }
    }

    private func loadArtwork(_ t: Track) {
        artwork = nil
        Task { @MainActor in
            let data = await Metadata.artwork(for: t.url)
            guard self.loadedTrack === t else { return }
            self.artwork = data.flatMap { NSImage(data: $0) }
            self.nowPlaying.update(self)
        }
    }

    // MARK: - BPM / key

    private func startAnalysis(_ t: Track) {
        analysisToken += 1
        let token = analysisToken
        guard !t.analyzed else { return }
        if let cached = analysisCache.get(t.url) { apply(cached, to: t); return }
        t.analyzing = true
        let url = t.url
        Task.detached(priority: .utility) { [analysisCache] in
            let result = (try? TrackAnalyzer.analyze(url: url)) ?? AnalysisResult()
            analysisCache.put(url, result)
            await MainActor.run {
                t.analyzing = false
                if token == self.analysisToken || t.analyzed == false { self.apply(result, to: t) }
            }
        }
        objectWillChange.send()
    }

    private func apply(_ r: AnalysisResult, to t: Track) {
        t.bpm = r.bpm
        t.key = r.key
        t.camelot = r.camelot
        t.analyzing = false
        t.analyzed = true
        objectWillChange.send()
    }

    // MARK: - playlist editing

    var selectedTracks: [Track] { tracks.filter { selection.contains($0.id) } }

    func removeSelected() { tracks.removeAll { selection.contains($0.id) }; selection.removeAll(); shuffleOrder = nil }
    func crop() { tracks.removeAll { !selection.contains($0.id) }; shuffleOrder = nil }
    func clearPlaylist() { tracks.removeAll(); selection.removeAll(); shuffleOrder = nil }
    func removeMissing() { tracks.removeAll { !FileManager.default.fileExists(atPath: $0.url.path) }; shuffleOrder = nil }

    func removeDuplicates() {
        var seen = Set<String>()
        tracks.removeAll { !seen.insert($0.url.standardizedFileURL.path.lowercased()).inserted }
        shuffleOrder = nil
    }

    func selectAll() { selection = Set(tracks.map(\.id)) }
    func invertSelection() { selection = Set(tracks.map(\.id)).subtracting(selection) }
    func move(from source: IndexSet, to destination: Int) { tracks.move(fromOffsets: source, toOffset: destination) }
    func reverse() { tracks.reverse() }
    func randomize() { tracks.shuffle(); shuffleOrder = nil }

    func sort<T: Comparable>(by key: (Track) -> T) { tracks.sort { key($0) < key($1) } }

    func showInFinder(_ list: [Track]) {
        NSWorkspace.shared.activateFileViewerSelecting(list.map(\.url))
    }

    func showFileInfo(_ t: Track?) {
        guard let t = t ?? selectedTracks.first ?? current else { return }
        Task { @MainActor in await Metadata.load(t); self.fileInfo = t }
    }

    func openPlaylistFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = ["m3u", "m3u8", "pls"].compactMap { UTType(filenameExtension: $0) }
        guard panel.runModal() == .OK, let url = panel.url else { return }
        open([url], play: false, replace: true)
    }

    func savePlaylistFile() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "playlist.m3u8"
        panel.allowedContentTypes = ["m3u8", "m3u", "pls"].compactMap { UTType(filenameExtension: $0) }
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try PlaylistIO.save(tracks, to: url) } catch { alert("Save failed", error.localizedDescription) }
    }

    // MARK: - Rekordbox

    var rekordboxXmlURL: URL { settings.rekordboxXmlPath.map(URL.init(fileURLWithPath:)) ?? RekordboxExport.defaultXmlURL }

    func exportToRekordbox(_ list: [Track]) {
        guard !rekordboxBusy else { showTransient("A Rekordbox export is already running…"); return }
        let supported = list.filter { RekordboxExport.isSupported($0) && FileManager.default.fileExists(atPath: $0.url.path) }
        let skipped = list.count - supported.count
        guard !supported.isEmpty else {
            alert("Export to Rekordbox", "Nothing to export. Rekordbox can load MP3, M4A/AAC, WAV, AIFF and FLAC files that exist on disk.")
            return
        }
        let xml = rekordboxXmlURL
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH:mm"
        let name = (list.count == tracks.count ? "Playlist " : "Selection ") + f.string(from: Date())
        rekordboxBusy = true
        showTransient("Rekordbox: preparing \(supported.count) track(s)…", seconds: 3)

        let cache = analysisCache
        Task.detached(priority: .userInitiated) {
            // Tags, then BPM/key for every track not analyzed yet (cached ones are instant), in parallel.
            for t in supported where !t.infoLoaded { await Metadata.load(t) }
            let todo = supported.filter { !$0.analyzed }
            let box = ResultsBox(count: todo.count)
            DispatchQueue.concurrentPerform(iterations: todo.count) { i in
                let url = todo[i].url
                let r = cache.get(url) ?? {
                    let r = (try? TrackAnalyzer.analyze(url: url)) ?? AnalysisResult()
                    cache.put(url, r)
                    return r
                }()
                let d = box.set(i, r)
                DispatchQueue.main.async { self.showTransient("Rekordbox: analyzing BPM & key \(d)/\(todo.count)…", seconds: 3) }
            }
            cache.save()
            let results = box.results
            await MainActor.run {
                for (i, t) in todo.enumerated() { if let r = results[i] { self.apply(r, to: t) } }
                self.rekordboxBusy = false
                do {
                    let result = try RekordboxExport.export(to: xml, tracks: supported, playlistName: name, appVersion: PlayerModel.version)
                    let note = skipped > 0 ? " (\(skipped) skipped: format rekordbox can't load)" : ""
                    self.showTransient("Exported \(supported.count) track(s) to Rekordbox\(note)", seconds: 5)
                    if !self.settings.rekordboxHelpShown || (RekordboxExport.rekordboxInstalled && !RekordboxExport.xmlSidebarEnabled) {
                        self.showRekordboxHelp(result)
                    }
                } catch {
                    self.alert("Rekordbox export failed", error.localizedDescription)
                }
            }
        }
    }

    func showRekordboxHelp(_ result: RekordboxExport.Result?) {
        let url = result?.xmlURL ?? rekordboxXmlURL
        rekordboxHelp = RekordboxHelpInfo(
            xmlURL: url, result: result,
            configuredInRekordbox: RekordboxExport.configuredXmlURL?.standardizedFileURL.path == url.standardizedFileURL.path,
            sidebarEnabled: RekordboxExport.xmlSidebarEnabled)
    }

    func chooseRekordboxFile() {
        let panel = NSSavePanel()
        panel.title = "Rekordbox XML export file"
        panel.allowedContentTypes = [.xml]
        panel.nameFieldStringValue = rekordboxXmlURL.lastPathComponent
        panel.directoryURL = rekordboxXmlURL.deletingLastPathComponent()
        guard panel.runModal() == .OK, let url = panel.url else { return }
        settings.rekordboxXmlPath = url.path == RekordboxExport.defaultXmlURL.path ? nil : url.path
        showTransient("Rekordbox exports go to \(url.lastPathComponent)", seconds: 3)
        saveSettingsSoon()
    }

    // MARK: - bundled sample

    static var sampleURL: URL { AppPaths.support.appendingPathComponent("Samples/Dunno Sample Shout.mp3") }

    /// Makes sure the bundled "Dunno Sample Shout" is in the playlist every time the app opens.
    func ensureSample() {
        let fm = FileManager.default
        let dest = Self.sampleURL
        if !fm.fileExists(atPath: dest.path) {
            guard let src = Bundle.main.url(forResource: "Dunno Sample Shout", withExtension: "mp3") else { return }
            try? fm.createDirectory(at: dest.deletingLastPathComponent(), withIntermediateDirectories: true)
            try? fm.copyItem(at: src, to: dest)
        }
        guard fm.fileExists(atPath: dest.path) else { return }
        let sample = tracks.first { $0.url.standardizedFileURL.path == dest.standardizedFileURL.path } ?? {
            let t = Track(url: dest)
            tracks.insert(t, at: 0)
            return t
        }()
        // The file's own tags name a different show; always present it as the SOUNDBOY sample.
        sample.fixedTitle = true
        sample.title = "Dunno Sample Shout"
        sample.artist = "SOUNDBOY"
        sample.album = "Samples"
        if !sample.infoLoaded { loadMetadata([sample]) }
    }

    // MARK: - persistence

    static var autosaveURL: URL { AppPaths.support.appendingPathComponent("playlist.m3u8") }

    private func loadAutosave() {
        guard let list = try? PlaylistIO.load(Self.autosaveURL) else { return }
        tracks = list
        if settings.currentIndex >= 0, settings.currentIndex < list.count { current = list[settings.currentIndex] }
        loadMetadata(list.filter { $0.duration == nil })
    }

    private var saveScheduled = false

    func saveSettingsSoon() {
        guard !saveScheduled else { return }
        saveScheduled = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) {
            self.saveScheduled = false
            self.settings.save()
        }
    }

    func saveState() {
        guard !isDemo else { return }
        settings.currentIndex = currentIndex ?? -1
        settings.save()
        try? PlaylistIO.save(tracks, to: Self.autosaveURL)
        analysisCache.save()
    }

    // MARK: - misc

    func alert(_ title: String, _ message: String) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = message
        a.runModal()
    }

    /// Fake state for UI snapshots (CI): no audio, fixed data.
    func loadDemo() {
        isDemo = true
        timer?.invalidate()
        let names: [(String, String, Double, Double?, String?)] = [
            ("Dunno Sample Shout", "SOUNDBOY", 1, nil, nil),
            ("Llama Groove", "The Synthesizers", 25, 112, "Am"),
            ("Night Drive (Extended Mix)", "Neon Ghost", 192, 96, "Em"),
            ("Tone Walk", "Lab", 271, 130, "C"),
            ("untitled_final_v2", "Unknown Artist", 280, nil, nil),
        ]
        tracks = names.map { n in
            let t = Track(url: URL(fileURLWithPath: "/Demo/\(n.0).mp3"))
            t.title = n.0; t.artist = n.1; t.duration = n.2; t.bpm = n.3; t.key = n.4
            t.camelot = n.4 == "Am" ? "8A" : n.4 == "Em" ? "9A" : n.4 == "C" ? "8B" : nil
            t.bitrate = 192; t.sampleRate = 44100; t.channels = 2; t.infoLoaded = true; t.analyzed = true
            t.album = "Demo"; t.year = 2026
            return t
        }
        current = tracks[1]
        loadedTrack = tracks[1]
        selection = [tracks[2].id]
        state = .playing
        duration = 25
        position = 9
        settings.eqBands = [4, 6, 3, 0, -2, -1, 2, 4, 5, 3]
        settings.shuffle = false
        settings.repeatMode = .all
        settings.volume = 0.75
        vis.setDemo()
    }

    var demoChips: Bool { isDemo }
}
