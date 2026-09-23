import Foundation
import SoundboyCore

enum RepeatMode: Int, Codable { case off, all, one }

/// Persisted to ~/Library/Application Support/SOUNDBOY/settings.json. Unknown/missing keys fall back to defaults.
struct AppSettings: Codable {
    var volume = 0.8
    var balance = 0.0
    var shuffle = false
    var repeatMode = RepeatMode.off
    var eqEnabled = true
    var eqPreamp: Float = 0
    var eqBands = [Float](repeating: 0, count: Equalizer.bandCount)
    var userPresets: [String: [Float]] = [:]
    var showEq = true
    var showPlaylist = true
    var compact = false
    var alwaysOnTop = false
    var vis = VisMode.spectrum
    var showPeaks = true
    var remainingTime = false
    var currentIndex = -1
    var rekordboxXmlPath: String?
    var rekordboxHelpShown = false

    init() {}

    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        volume = (try? c.decodeIfPresent(Double.self, forKey: .volume)) ?? volume
        balance = (try? c.decodeIfPresent(Double.self, forKey: .balance)) ?? balance
        shuffle = (try? c.decodeIfPresent(Bool.self, forKey: .shuffle)) ?? shuffle
        repeatMode = (try? c.decodeIfPresent(RepeatMode.self, forKey: .repeatMode)) ?? repeatMode
        eqEnabled = (try? c.decodeIfPresent(Bool.self, forKey: .eqEnabled)) ?? eqEnabled
        eqPreamp = (try? c.decodeIfPresent(Float.self, forKey: .eqPreamp)) ?? eqPreamp
        eqBands = (try? c.decodeIfPresent([Float].self, forKey: .eqBands)) ?? eqBands
        if eqBands.count != Equalizer.bandCount { eqBands = [Float](repeating: 0, count: Equalizer.bandCount) }
        userPresets = (try? c.decodeIfPresent([String: [Float]].self, forKey: .userPresets)) ?? userPresets
        showEq = (try? c.decodeIfPresent(Bool.self, forKey: .showEq)) ?? showEq
        showPlaylist = (try? c.decodeIfPresent(Bool.self, forKey: .showPlaylist)) ?? showPlaylist
        compact = (try? c.decodeIfPresent(Bool.self, forKey: .compact)) ?? compact
        alwaysOnTop = (try? c.decodeIfPresent(Bool.self, forKey: .alwaysOnTop)) ?? alwaysOnTop
        vis = (try? c.decodeIfPresent(VisMode.self, forKey: .vis)) ?? vis
        showPeaks = (try? c.decodeIfPresent(Bool.self, forKey: .showPeaks)) ?? showPeaks
        remainingTime = (try? c.decodeIfPresent(Bool.self, forKey: .remainingTime)) ?? remainingTime
        currentIndex = (try? c.decodeIfPresent(Int.self, forKey: .currentIndex)) ?? currentIndex
        rekordboxXmlPath = (try? c.decodeIfPresent(String.self, forKey: .rekordboxXmlPath)) ?? nil
        rekordboxHelpShown = (try? c.decodeIfPresent(Bool.self, forKey: .rekordboxHelpShown)) ?? rekordboxHelpShown
    }

    static var file: URL { AppPaths.support.appendingPathComponent("settings.json") }

    static func load() -> AppSettings {
        guard let data = try? Data(contentsOf: file) else { return AppSettings() }
        return (try? JSONDecoder().decode(AppSettings.self, from: data)) ?? AppSettings()
    }

    func save() {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        try? enc.encode(self).write(to: Self.file, options: .atomic)
    }
}
