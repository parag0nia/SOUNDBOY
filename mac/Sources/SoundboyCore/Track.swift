import Foundation

/// A playlist entry. Reference type so the UI, the playlist and background loaders share one instance.
public final class Track: Identifiable, Hashable, @unchecked Sendable {
    public let id = UUID()
    public let url: URL

    public var title: String?
    public var artist: String?
    public var album: String?
    public var genre: String?
    public var year = 0
    public var duration: TimeInterval?
    public var bitrate = 0      // kbps
    public var sampleRate = 0   // Hz
    public var channels = 0

    /// BPM stored in the file's tags (e.g. by DJ software); preferred over detection.
    public var tagBpm: Double?
    public var bpm: Double?
    public var key: String?
    public var camelot: String?
    public var analyzing = false
    public var analyzed = false

    public var infoLoaded = false
    public var missing = false
    /// When set, embedded tags never override title/artist (used for the bundled sample).
    public var fixedTitle = false

    public init(url: URL) { self.url = url }

    public static func == (a: Track, b: Track) -> Bool { a === b }
    public func hash(into h: inout Hasher) { h.combine(ObjectIdentifier(self)) }

    public var fileExtension: String { url.pathExtension.lowercased() }
    public var fileNameTitle: String { url.deletingPathExtension().lastPathComponent }

    public var displayTitle: String {
        guard let t = title, !t.isEmpty else { return fileNameTitle }
        if let a = artist, !a.isEmpty { return "\(a) - \(t)" }
        return t
    }

    /// Title without the artist (the player shows the artist on its own line).
    public var displayTitleOnly: String {
        if let t = title, !t.isEmpty { return t }
        return fileNameTitle
    }

    public var displayBpm: Double? { tagBpm ?? bpm }
}

public enum AppPaths {
    /// Optional isolated profile (env var SOUNDBOY_PROFILE) with its own data folder, for tests.
    public static var profile: String {
        let raw = ProcessInfo.processInfo.environment["SOUNDBOY_PROFILE"] ?? ""
        return String(raw.filter { $0.isLetter || $0.isNumber })
    }

    /// ~/Library/Application Support/SOUNDBOY
    public static var support: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let url = base.appendingPathComponent(profile.isEmpty ? "SOUNDBOY" : "SOUNDBOY-\(profile)", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }
}

public enum TimeFormat {
    public static func clock(_ t: TimeInterval) -> String {
        let s = max(0, Int(t.isFinite ? t : 0))
        return s >= 3600
            ? String(format: "%d:%02d:%02d", s / 3600, (s / 60) % 60, s % 60)
            : String(format: "%d:%02d", s / 60, s % 60)
    }

    /// For track lengths: a 0.9 s clip reads "0:01", not "0:00".
    public static func length(_ t: TimeInterval) -> String {
        clock(t > 0 && t < 1 ? 1 : t)
    }
}
