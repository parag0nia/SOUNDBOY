import AVFoundation
import XCTest
@testable import SoundboyCore

final class CoreTests: XCTestCase {
    var dir: URL!

    override func setUpWithError() throws {
        dir = FileManager.default.temporaryDirectory.appendingPathComponent("soundboy-tests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: dir) }

    /// Kick on every beat, hat on the off-beat, a melody on eighth notes and a bass line (same generator as the Windows tests).
    func synth(name: String, bpm: Double, notes: [Int], seconds: Double = 25) throws -> URL {
        let url = dir.appendingPathComponent(name)
        let sr = 44100.0
        let format = AVAudioFormat(standardFormatWithSampleRate: sr, channels: 2)!
        let file = try AVAudioFile(forWriting: url, settings: format.settings)
        let frames = AVAudioFrameCount(sr * seconds)
        let buf = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: frames)!
        buf.frameLength = frames
        let beat = 60 / bpm
        var rng = SystemRandomNumberGenerator()
        for i in 0..<Int(frames) {
            let t = Double(i) / sr
            let step = Int(t / (beat / 2)), st = t.truncatingRemainder(dividingBy: beat / 2)
            let f = 440 * pow(2, Double(notes[step % notes.count] - 69) / 12)
            let env = exp(-st * 6)
            let lead = (sin(2 * .pi * f * t) + 0.4 * sin(4 * .pi * f * t) + 0.2 * sin(6 * .pi * f * t)) * env * 0.25
            let bassF = 440 * pow(2, Double(notes[(step / 4) % notes.count] - 12 - 69) / 12)
            let bass = sin(2 * .pi * bassF * t) * 0.3
            let kt = t.truncatingRemainder(dividingBy: beat)
            let kick = sin(2 * .pi * (50 + 120 * exp(-kt * 30)) * kt) * exp(-kt * 9) * 0.5
            let ht = (t + beat / 2).truncatingRemainder(dividingBy: beat)
            let hat = (Double.random(in: -1...1, using: &rng)) * exp(-ht * 40) * 0.12
            let v = Float((bass + kick + hat + lead) * 0.8)
            buf.floatChannelData![0][i] = v
            buf.floatChannelData![1][i] = v
        }
        try file.write(from: buf)
        return url
    }

    func testTempoAndKey() throws {
        let cases: [(String, Double, [Int], String, String)] = [
            ("llama.wav", 112, [45, 52, 57, 60, 64, 57, 52, 48], "Am", "8A"),
            ("drive.wav", 96, [40, 47, 52, 55, 59, 52, 47, 43], "Em", "9A"),
            ("walk.wav", 130, [48, 55, 60, 64, 67, 72, 76, 79], "C", "8B"),
        ]
        for (name, bpm, notes, key, camelot) in cases {
            let url = try synth(name: name, bpm: bpm, notes: notes)
            let r = try TrackAnalyzer.analyze(url: url)
            XCTAssertNotNil(r.bpm, name)
            XCTAssertEqual(r.bpm ?? 0, bpm, accuracy: 1.0, "\(name) BPM")
            XCTAssertEqual(r.key, key, "\(name) key")
            XCTAssertEqual(r.camelot, camelot, "\(name) camelot")
        }
    }

    func testShortClipHasNoTempo() throws {
        let url = try synth(name: "short.wav", bpm: 120, notes: [57, 60, 64], seconds: 1)
        XCTAssertEqual(try TrackAnalyzer.analyze(url: url), AnalysisResult())
    }

    func testRekordboxLocationEncoding() {
        let url = URL(fileURLWithPath: "/Users/me/Music/Sub Folder (Remixes)/Night Drive & Co – Ünïcode.m4a")
        XCTAssertEqual(RekordboxExport.location(for: url),
                       "file://localhost/Users/me/Music/Sub%20Folder%20%28Remixes%29/Night%20Drive%20%26%20Co%20%E2%80%93%20%C3%9Cn%C3%AFcode.m4a")
    }

    func testRekordboxExportMergesAndPreservesForeignContent() throws {
        let a = try synth(name: "a.wav", bpm: 120, notes: [57], seconds: 2)
        let b = try synth(name: "b b.wav", bpm: 120, notes: [57], seconds: 2)
        let tracks = [a, b].map { url -> Track in
            let t = Track(url: url); t.title = url.lastPathComponent; t.bpm = 123.4; t.key = "Am"; return t
        }
        let xml = dir.appendingPathComponent("rb.xml")
        let r1 = try RekordboxExport.export(to: xml, tracks: [tracks[0]], playlistName: "One", appVersion: "2.1")
        let r2 = try RekordboxExport.export(to: xml, tracks: tracks, playlistName: "Two", appVersion: "2.1")
        XCTAssertEqual([r1.added, r1.updated, r2.added, r2.updated], [1, 0, 1, 1])
        XCTAssertTrue(r1.createdFile)
        XCTAssertFalse(r2.createdFile)

        let doc = try XMLDocument(contentsOf: xml, options: [])
        let tracksXml = try doc.nodes(forXPath: "/DJ_PLAYLISTS/COLLECTION/TRACK") as! [XMLElement]
        XCTAssertEqual(tracksXml.count, 2)
        XCTAssertEqual(tracksXml[0].attribute(forName: "AverageBpm")?.stringValue, "123.40")
        XCTAssertEqual(tracksXml[0].attribute(forName: "Tonality")?.stringValue, "Am")
        let lists = try doc.nodes(forXPath: "//NODE[@Name='SOUNDBOY']/NODE") as! [XMLElement]
        XCTAssertEqual(lists.map { $0.attribute(forName: "Name")!.stringValue! }, ["All SOUNDBOY exports", "Two", "One"])

        // Another app's library: its tracks and playlists survive and it's backed up first.
        let foreign = dir.appendingPathComponent("foreign.xml")
        try #"<?xml version="1.0" encoding="UTF-8"?><DJ_PLAYLISTS Version="1.0.0"><PRODUCT Name="rekordbox"/><COLLECTION Entries="1"><TRACK TrackID="77" Name="Theirs" Location="file://localhost/x.mp3"/></COLLECTION><PLAYLISTS><NODE Type="0" Name="ROOT" Count="1"><NODE Name="Their List" Type="1" KeyType="0" Entries="1"><TRACK Key="77"/></NODE></NODE></PLAYLISTS></DJ_PLAYLISTS>"#
            .write(to: foreign, atomically: true, encoding: .utf8)
        _ = try RekordboxExport.export(to: foreign, tracks: [tracks[0]], playlistName: "Mine", appVersion: "2.1")
        let f = try XMLDocument(contentsOf: foreign, options: [])
        XCTAssertEqual(try f.nodes(forXPath: "//TRACK[@Name='Theirs']").count, 1)
        XCTAssertEqual(try f.nodes(forXPath: "//NODE[@Name='Their List']").count, 1)
        XCTAssertEqual((try f.nodes(forXPath: "/DJ_PLAYLISTS/COLLECTION/TRACK").last as? XMLElement)?.attribute(forName: "TrackID")?.stringValue, "78")
        XCTAssertTrue(FileManager.default.fileExists(atPath: foreign.path + ".before-soundboy.bak"))
    }

    func testEqfRoundTrip() throws {
        let url = dir.appendingPathComponent("p.eqf")
        let presets = [EqPreset(name: "Test", bands: [12, -12, 0, 6, -6, 3, -3, 0, 9, -9], preamp: -3)]
        try EqPresets.writeEqf(presets, to: url)
        let back = try EqPresets.readEqf(url)
        XCTAssertEqual(back.count, 1)
        XCTAssertEqual(back[0].name, "Test")
        for (x, y) in zip(back[0].bands, presets[0].bands) { XCTAssertEqual(x, y, accuracy: 0.5) }
        XCTAssertEqual(back[0].preamp, -3, accuracy: 0.5)
    }

    func testPlaylistRoundTrip() throws {
        let a = try synth(name: "one.wav", bpm: 120, notes: [57], seconds: 1)
        let t = Track(url: a); t.title = "One"; t.artist = "Artist"; t.duration = 61
        let m3u = dir.appendingPathComponent("list.m3u8")
        try PlaylistIO.save([t], to: m3u)
        let back = try PlaylistIO.load(m3u)
        XCTAssertEqual(back.count, 1)
        XCTAssertEqual(back[0].url.standardizedFileURL.path, a.standardizedFileURL.path)
        XCTAssertEqual(back[0].title, "Artist - One")
        XCTAssertEqual(back[0].duration, 61)
        XCTAssertEqual(MediaFiles.expand([dir]).count, 1)
    }

    func testTimeFormat() {
        XCTAssertEqual(TimeFormat.clock(65), "1:05")
        XCTAssertEqual(TimeFormat.clock(3725), "1:02:05")
        XCTAssertEqual(TimeFormat.length(0.9), "0:01")
    }
}
