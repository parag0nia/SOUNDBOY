import Foundation

/// Writes tracks into a rekordbox XML library (the DJ_PLAYLISTS format rekordbox imports via its
/// "rekordbox xml" sidebar). Existing content is merged, never discarded: tracks are matched by file
/// location, and SOUNDBOY only touches its own "SOUNDBOY" playlist folder.
public enum RekordboxExport {
    public struct Result: Sendable {
        public let xmlURL: URL
        public let added: Int
        public let updated: Int
        public let playlistName: String
        public let createdFile: Bool
    }

    static let folderName = "SOUNDBOY"
    static let allPlaylistName = "All SOUNDBOY exports"

    static let kinds: [String: String] = [
        "mp3": "MP3 File", "m4a": "M4A File", "aac": "M4A File", "mp4": "M4A File",
        "wav": "WAV File", "aif": "AIFF File", "aiff": "AIFF File", "flac": "FLAC File",
    ]

    /// Formats rekordbox can load.
    public static func isSupported(_ t: Track) -> Bool { kinds[t.fileExtension] != nil }

    static var settingsCandidates: [URL] {
        let lib = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library")
        return [
            lib.appendingPathComponent("Application Support/Pioneer/rekordbox6/rekordbox3.settings"),
            lib.appendingPathComponent("Pioneer/rekordbox6/rekordbox3.settings"),
            lib.appendingPathComponent("Application Support/Pioneer/rekordbox/rekordbox3.settings"),
        ]
    }

    static func readSetting(_ name: String) -> String? {
        for url in settingsCandidates where FileManager.default.fileExists(atPath: url.path) {
            guard let doc = try? XMLDocument(contentsOf: url, options: []),
                  let nodes = try? doc.nodes(forXPath: "//VALUE[@name='\(name)']"),
                  let el = nodes.first as? XMLElement else { continue }
            return el.attribute(forName: "val")?.stringValue
        }
        return nil
    }

    public static var rekordboxInstalled: Bool {
        settingsCandidates.contains { FileManager.default.fileExists(atPath: $0.path) }
    }

    /// The XML file rekordbox is set to read (Preferences › Advanced › Database › rekordbox xml).
    public static var configuredXmlURL: URL? {
        guard let v = readSetting("bridgeImportedLibraryFile"), !v.isEmpty else { return nil }
        return URL(fileURLWithPath: v)
    }

    /// Whether "rekordbox xml" is shown in rekordbox's sidebar (Preferences › View › Layout).
    public static var xmlSidebarEnabled: Bool { readSetting("showRbXml") == "1" }

    public static var defaultXmlURL: URL {
        configuredXmlURL ?? FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Music/SOUNDBOY/SOUNDBOY-rekordbox.xml")
    }

    public static func export(to url: URL, tracks: [Track], playlistName: String, appVersion: String) throws -> Result {
        let fm = FileManager.default
        let existed = fm.fileExists(atPath: url.path)
        let doc: XMLDocument
        if existed {
            do { doc = try XMLDocument(contentsOf: url, options: [.nodePreserveWhitespace]) }
            catch { throw err("The existing rekordbox XML file can't be read (\(error.localizedDescription)). It was left untouched.") }
            guard doc.rootElement()?.name == "DJ_PLAYLISTS" else {
                throw err("The existing file isn't a rekordbox XML library. It was left untouched.")
            }
            // Back up a library written by another app before SOUNDBOY first modifies it.
            let product = doc.rootElement()?.elements(forName: "PRODUCT").first?.attribute(forName: "Name")?.stringValue
            let backup = URL(fileURLWithPath: url.path + ".before-soundboy.bak")
            if product != "SOUNDBOY", !fm.fileExists(atPath: backup.path) { try fm.copyItem(at: url, to: backup) }
        } else {
            let root = XMLElement(name: "DJ_PLAYLISTS")
            set(root, "Version", "1.0.0")
            let product = XMLElement(name: "PRODUCT")
            set(product, "Name", "SOUNDBOY"); set(product, "Version", appVersion); set(product, "Company", "SOUNDBOY")
            root.addChild(product)
            doc = XMLDocument(rootElement: root)
            doc.version = "1.0"
            doc.characterEncoding = "UTF-8"
        }

        let root = doc.rootElement()!
        let collection = root.elements(forName: "COLLECTION").first ?? add(root, "COLLECTION")
        let playlists = root.elements(forName: "PLAYLISTS").first ?? add(root, "PLAYLISTS")
        let rootNode = playlists.elements(forName: "NODE").first { $0.attribute(forName: "Name")?.stringValue == "ROOT" } ?? {
            let n = add(playlists, "NODE")
            set(n, "Type", "0"); set(n, "Name", "ROOT"); set(n, "Count", "0")
            return n
        }()

        // Existing tracks by location, and the next free TrackID.
        var byLocation: [String: XMLElement] = [:]
        var nextId = 1
        for t in collection.elements(forName: "TRACK") {
            if let loc = t.attribute(forName: "Location")?.stringValue { byLocation[loc.lowercased()] = t }
            if let s = t.attribute(forName: "TrackID")?.stringValue, let id = Int(s) { nextId = max(nextId, id + 1) }
        }

        var added = 0, updated = 0
        var ids: [String] = []
        var seen = Set<String>()
        for t in tracks where isSupported(t) && seen.insert(t.url.path.lowercased()).inserted {
            let loc = location(for: t.url)
            let el: XMLElement
            if let existing = byLocation[loc.lowercased()] {
                el = existing
                fill(el, t, isNew: false)
                updated += 1
            } else {
                el = XMLElement(name: "TRACK")
                set(el, "TrackID", String(nextId)); nextId += 1
                fill(el, t, isNew: true)
                collection.addChild(el)
                byLocation[loc.lowercased()] = el
                added += 1
            }
            ids.append(el.attribute(forName: "TrackID")!.stringValue!)
        }
        set(collection, "Entries", String(collection.elements(forName: "TRACK").count))

        // ROOT › SOUNDBOY › [this export] and [All SOUNDBOY exports]
        let folder = rootNode.elements(forName: "NODE").first {
            $0.attribute(forName: "Type")?.stringValue == "0" && $0.attribute(forName: "Name")?.stringValue == folderName
        } ?? {
            let n = add(rootNode, "NODE")
            set(n, "Type", "0"); set(n, "Name", folderName); set(n, "Count", "0")
            return n
        }()

        let all = playlist(in: folder, named: allPlaylistName, keepExisting: true)
        var existing = Set(all.elements(forName: "TRACK").compactMap { $0.attribute(forName: "Key")?.stringValue })
        for id in ids where existing.insert(id).inserted { all.addChild(trackRef(id)) }
        set(all, "Entries", String(all.elements(forName: "TRACK").count))

        let list = playlist(in: folder, named: playlistName, keepExisting: false)
        for id in ids { list.addChild(trackRef(id)) }
        set(list, "Entries", String(ids.count))

        set(folder, "Count", String(folder.elements(forName: "NODE").count))
        set(rootNode, "Count", String(rootNode.elements(forName: "NODE").count))

        try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try doc.xmlData(options: [.nodePrettyPrint]).write(to: url, options: .atomic)
        return Result(xmlURL: url, added: added, updated: updated, playlistName: playlistName, createdFile: !existed)
    }

    // MARK: helpers

    static func err(_ message: String) -> NSError {
        NSError(domain: "SOUNDBOY", code: 2, userInfo: [NSLocalizedDescriptionKey: message])
    }

    static func set(_ el: XMLElement, _ name: String, _ value: String) {
        if let a = el.attribute(forName: name) { a.stringValue = value }
        else { el.addAttribute(XMLNode.attribute(withName: name, stringValue: value) as! XMLNode) }
    }

    @discardableResult
    static func add(_ parent: XMLElement, _ name: String) -> XMLElement {
        let el = XMLElement(name: name)
        parent.addChild(el)
        return el
    }

    static func trackRef(_ id: String) -> XMLElement {
        let el = XMLElement(name: "TRACK")
        set(el, "Key", id)
        return el
    }

    static func playlist(in folder: XMLElement, named name: String, keepExisting: Bool) -> XMLElement {
        let nodes = folder.elements(forName: "NODE")
        var node = nodes.first { $0.attribute(forName: "Type")?.stringValue == "1" && $0.attribute(forName: "Name")?.stringValue == name }
        if let n = node, !keepExisting { n.detach(); node = nil }
        if let node { return node }

        let n = XMLElement(name: "NODE")
        set(n, "Name", name); set(n, "Type", "1"); set(n, "KeyType", "0"); set(n, "Entries", "0")
        // Keep "All SOUNDBOY exports" first, then exports newest-first.
        let first = folder.elements(forName: "NODE").first
        if name != allPlaylistName, let first, first.attribute(forName: "Name")?.stringValue == allPlaylistName {
            folder.insertChild(n, at: first.index + 1)
        } else {
            folder.insertChild(n, at: 0)
        }
        return n
    }

    static func fill(_ el: XMLElement, _ t: Track, isNew: Bool) {
        let attrs = try? FileManager.default.attributesOfItem(atPath: t.url.path)
        let size = (attrs?[.size] as? NSNumber)?.int64Value ?? 0
        set(el, "Name", t.title?.isEmpty == false ? t.title! : t.fileNameTitle)
        set(el, "Artist", t.artist ?? "")
        set(el, "Album", t.album ?? "")
        set(el, "Genre", t.genre ?? "")
        set(el, "Kind", kinds[t.fileExtension] ?? "")
        set(el, "Size", String(size))
        set(el, "TotalTime", String(Int((t.duration ?? 0).rounded())))
        set(el, "Year", String(t.year))
        if let bpm = t.displayBpm { set(el, "AverageBpm", String(format: "%.2f", locale: Locale(identifier: "en_US_POSIX"), bpm)) }
        if let key = t.key { set(el, "Tonality", key) }
        if t.bitrate > 0 { set(el, "BitRate", String(t.bitrate)) }
        if t.sampleRate > 0 { set(el, "SampleRate", String(t.sampleRate)) }
        if isNew {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = "yyyy-MM-dd"
            set(el, "DateAdded", f.string(from: Date()))
        }
        set(el, "Location", location(for: t.url))
    }

    /// rekordbox location format on macOS: file://localhost/Users/me/Music/My%20Track.mp3
    public static func location(for url: URL) -> String {
        let allowed = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
        let parts = url.standardizedFileURL.path.split(separator: "/", omittingEmptySubsequences: true)
            .map { String($0).addingPercentEncoding(withAllowedCharacters: allowed) ?? String($0) }
        return "file://localhost/" + parts.joined(separator: "/")
    }
}
