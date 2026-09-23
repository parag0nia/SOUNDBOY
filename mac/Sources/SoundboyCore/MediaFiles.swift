import Foundation

public enum MediaFiles {
    /// Formats Core Audio decodes natively (no OGG/WMA on macOS).
    public static let audioExtensions: Set<String> = [
        "mp3", "m4a", "m4b", "aac", "mp4", "wav", "aif", "aiff", "aifc", "caf", "flac",
    ]
    public static let playlistExtensions: Set<String> = ["m3u", "m3u8", "pls"]

    public static func isAudio(_ url: URL) -> Bool { audioExtensions.contains(url.pathExtension.lowercased()) }
    public static func isPlaylist(_ url: URL) -> Bool { playlistExtensions.contains(url.pathExtension.lowercased()) }

    /// Turns files, folders (recursive) and playlists into tracks.
    public static func expand(_ urls: [URL]) -> [Track] {
        var result: [Track] = []
        let fm = FileManager.default
        for url in urls {
            var isDir: ObjCBool = false
            guard fm.fileExists(atPath: url.path, isDirectory: &isDir) else { continue }
            if isDir.boolValue {
                var files: [URL] = []
                if let e = fm.enumerator(at: url, includingPropertiesForKeys: [.isRegularFileKey], options: [.skipsHiddenFiles]) {
                    for case let f as URL in e where isAudio(f) { files.append(f) }
                }
                files.sort { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
                result += files.map(Track.init)
            } else if isPlaylist(url) {
                result += (try? PlaylistIO.load(url)) ?? []
            } else if isAudio(url) {
                result.append(Track(url: url))
            }
        }
        return result
    }
}

public enum PlaylistIO {
    public static func load(_ url: URL) throws -> [Track] {
        let data = try Data(contentsOf: url)
        let text = String(data: data, encoding: .utf8) ?? String(decoding: data, as: UTF8.self)
        let lines = text.replacingOccurrences(of: "\u{FEFF}", with: "").components(separatedBy: .newlines)
        let dir = url.deletingLastPathComponent()
        return url.pathExtension.lowercased() == "pls" ? loadPls(lines, dir) : loadM3u(lines, dir)
    }

    static func loadM3u(_ lines: [String], _ dir: URL) -> [Track] {
        var list: [Track] = []
        var title: String?
        var duration: TimeInterval?
        for raw in lines {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty { continue }
            if line.uppercased().hasPrefix("#EXTINF:") {
                let body = String(line.dropFirst(8))
                if let comma = body.firstIndex(of: ",") {
                    let secs = body[..<comma].split(separator: " ").first.map(String.init) ?? ""
                    if let s = Double(secs), s > 0 { duration = s }
                    title = String(body[body.index(after: comma)...]).trimmingCharacters(in: .whitespaces)
                }
                continue
            }
            if line.hasPrefix("#") { continue }
            if let t = makeTrack(line, dir, title, duration) { list.append(t) }
            title = nil
            duration = nil
        }
        return list
    }

    static func loadPls(_ lines: [String], _ dir: URL) -> [Track] {
        var files: [Int: String] = [:], titles: [Int: String] = [:], lengths: [Int: Double] = [:]
        for raw in lines {
            guard let eq = raw.firstIndex(of: "=") else { continue }
            let key = raw[..<eq].trimmingCharacters(in: .whitespaces).lowercased()
            let val = raw[raw.index(after: eq)...].trimmingCharacters(in: .whitespaces)
            for (prefix, apply) in [("file", { (n: Int) in files[n] = val }),
                                    ("title", { (n: Int) in titles[n] = val }),
                                    ("length", { (n: Int) in lengths[n] = Double(val) })] {
                if key.hasPrefix(prefix), let n = Int(key.dropFirst(prefix.count)) { apply(n) }
            }
        }
        return files.keys.sorted().compactMap { n in
            makeTrack(files[n]!, dir, titles[n], lengths[n].flatMap { $0 > 0 ? $0 : nil })
        }
    }

    static func makeTrack(_ entry: String, _ dir: URL, _ title: String?, _ duration: TimeInterval?) -> Track? {
        let url: URL
        if entry.lowercased().hasPrefix("file:") {
            guard let u = URL(string: entry) else { return nil }
            url = u
        } else if entry.hasPrefix("/") {
            url = URL(fileURLWithPath: entry)
        } else if entry.contains("://") {
            return nil // streams are not supported on macOS yet
        } else {
            url = dir.appendingPathComponent(entry.replacingOccurrences(of: "\\", with: "/")).standardizedFileURL
        }
        let t = Track(url: url)
        t.duration = duration
        if let title, !title.isEmpty { t.title = title }
        return t
    }

    public static func save(_ tracks: [Track], to url: URL) throws {
        var out = ""
        if url.pathExtension.lowercased() == "pls" {
            out += "[playlist]\n"
            for (i, t) in tracks.enumerated() {
                out += "File\(i + 1)=\(t.url.path)\nTitle\(i + 1)=\(t.displayTitle)\nLength\(i + 1)=\(Int(t.duration ?? -1))\n"
            }
            out += "NumberOfEntries=\(tracks.count)\nVersion=2\n"
        } else {
            out += "#EXTM3U\n"
            for t in tracks {
                out += "#EXTINF:\(t.duration.map { Int($0) } ?? -1),\(t.displayTitle)\n\(t.url.path)\n"
            }
        }
        try out.write(to: url, atomically: true, encoding: .utf8)
    }
}
