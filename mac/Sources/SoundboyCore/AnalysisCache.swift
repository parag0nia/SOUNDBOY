import Foundation

/// Remembers BPM/key results per file (keyed by path + size + modified time) in
/// ~/Library/Application Support/SOUNDBOY/analysis.json so each file is analyzed only once.
public final class AnalysisCache: @unchecked Sendable {
    private var entries: [String: AnalysisResult]
    private let lock = NSLock()
    private let file: URL

    public init(directory: URL = AppPaths.support) {
        file = directory.appendingPathComponent("analysis.json")
        entries = (try? JSONDecoder().decode([String: AnalysisResult].self, from: Data(contentsOf: file))) ?? [:]
    }

    static func key(for url: URL) -> String? {
        guard let a = try? FileManager.default.attributesOfItem(atPath: url.path),
              let size = a[.size] as? NSNumber, let date = a[.modificationDate] as? Date else { return nil }
        return "\(url.standardizedFileURL.path.lowercased())|\(size.int64Value)|\(Int64(date.timeIntervalSince1970 * 1000))"
    }

    public func get(_ url: URL) -> AnalysisResult? {
        guard let k = Self.key(for: url) else { return nil }
        lock.lock(); defer { lock.unlock() }
        return entries[k]
    }

    public func put(_ url: URL, _ r: AnalysisResult) {
        guard let k = Self.key(for: url) else { return }
        lock.lock(); defer { lock.unlock() }
        entries[k] = r
    }

    public func save() {
        lock.lock()
        let data = try? JSONEncoder().encode(entries)
        lock.unlock()
        try? data?.write(to: file, options: .atomic)
    }
}
