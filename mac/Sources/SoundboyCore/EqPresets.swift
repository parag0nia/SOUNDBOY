import Foundation

public enum Equalizer {
    public static let bandCount = 10
    public static let maxDb: Float = 12
    public static let frequencies: [Float] = [60, 170, 310, 600, 1000, 3000, 6000, 12000, 14000, 16000]
    public static let labels = ["60", "170", "310", "600", "1K", "3K", "6K", "12K", "14K", "16K"]
}

public struct EqPreset: Equatable {
    public var name: String
    public var bands: [Float]
    public var preamp: Float
    public init(name: String, bands: [Float], preamp: Float) { self.name = name; self.bands = bands; self.preamp = preamp }
}

public enum EqPresets {
    public static let builtIn: [EqPreset] = [
        .init(name: "Classical", bands: [0, 0, 0, 0, 0, 0, -7.2, -7.2, -7.2, -9.6], preamp: 0),
        .init(name: "Club", bands: [0, 0, 8, 5.6, 5.6, 5.6, 3.2, 0, 0, 0], preamp: 0),
        .init(name: "Dance", bands: [9.6, 7.2, 2.4, 0, 0, -5.6, -7.2, -7.2, 0, 0], preamp: 0),
        .init(name: "Full Bass", bands: [-8, 9.6, 9.6, 5.6, 1.6, -4, -8, -10.4, -11.2, -11.2], preamp: -3),
        .init(name: "Full Bass & Treble", bands: [7.2, 5.6, 0, -7.2, -4.8, 1.6, 8, 11.2, 12, 12], preamp: -3),
        .init(name: "Full Treble", bands: [-9.6, -9.6, -9.6, -4, 2.4, 11.2, 12, 12, 12, 12], preamp: -3),
        .init(name: "Laptop Speakers", bands: [4.8, 11.2, 5.6, -3.2, -2.4, 1.6, 4.8, 9.6, 12, 12], preamp: -3),
        .init(name: "Large Hall", bands: [10.4, 10.4, 5.6, 5.6, 0, -4.8, -4.8, -4.8, 0, 0], preamp: -2),
        .init(name: "Live", bands: [-4.8, 0, 4, 5.6, 5.6, 5.6, 4, 2.4, 2.4, 2.4], preamp: 0),
        .init(name: "Party", bands: [7.2, 7.2, 0, 0, 0, 0, 0, 0, 7.2, 7.2], preamp: 0),
        .init(name: "Pop", bands: [-1.6, 4.8, 7.2, 8, 5.6, 0, -2.4, -2.4, -1.6, -1.6], preamp: 0),
        .init(name: "Reggae", bands: [0, 0, 0, -5.6, 0, 6.4, 6.4, 0, 0, 0], preamp: 0),
        .init(name: "Rock", bands: [8, 4.8, -5.6, -8, -3.2, 4, 8.8, 11.2, 11.2, 11.2], preamp: -3),
        .init(name: "Ska", bands: [-2.4, -4.8, -4, 0, 4, 5.6, 8.8, 9.6, 11.2, 9.6], preamp: -2),
        .init(name: "Soft", bands: [4.8, 1.6, 0, -2.4, 0, 4, 8, 9.6, 11.2, 12], preamp: -2),
        .init(name: "Soft Rock", bands: [4, 4, 2.4, 0, -4, -5.6, -3.2, 0, 2.4, 8.8], preamp: 0),
        .init(name: "Techno", bands: [8, 5.6, 0, -5.6, -4.8, 0, 8, 9.6, 9.6, 8.8], preamp: -2),
    ]

    // Winamp .EQF: 31-byte header, then records of 257-byte name + 11 bytes (10 bands, preamp).
    // Each byte is a slider position 0..63 where 0 is the top (+12 dB) and 31 is 0 dB.
    static let header = Array("Winamp EQ library file v1.1\u{1A}!--".utf8)
    static let nameLength = 257

    static func toDb(_ b: UInt8) -> Float { min(12, max(-12, (31 - Float(b)) / 31 * 12)) }
    static func toByte(_ db: Float) -> UInt8 { UInt8(min(63, max(0, (31 - db / 12 * 31).rounded()))) }

    public static func readEqf(_ url: URL) throws -> [EqPreset] {
        let data = [UInt8](try Data(contentsOf: url))
        guard data.count >= header.count, Array(data[0..<27]) == Array(header[0..<27]) else {
            throw NSError(domain: "SOUNDBOY", code: 1, userInfo: [NSLocalizedDescriptionKey: "Not a Winamp EQ library (.eqf) file."])
        }
        var list: [EqPreset] = []
        var pos = header.count
        while pos + nameLength + 11 <= data.count {
            let nameBytes = data[pos..<(pos + nameLength)].prefix { $0 != 0 }
            let name = String(decoding: nameBytes, as: UTF8.self).trimmingCharacters(in: .whitespaces)
            let bands = (0..<10).map { toDb(data[pos + nameLength + $0]) }
            list.append(EqPreset(name: name.isEmpty ? "Imported" : name, bands: bands, preamp: toDb(data[pos + nameLength + 10])))
            pos += nameLength + 11
        }
        return list
    }

    public static func writeEqf(_ presets: [EqPreset], to url: URL) throws {
        var out = header
        for p in presets {
            var name = [UInt8](repeating: 0, count: nameLength)
            let raw = Array(p.name.utf8.prefix(nameLength - 1))
            name.replaceSubrange(0..<raw.count, with: raw)
            out += name
            out += (0..<10).map { toByte(p.bands[$0]) }
            out.append(toByte(p.preamp))
        }
        try Data(out).write(to: url)
    }
}
