import AppKit
import SoundboyCore
import SwiftUI

struct ContentView: View {
    static let titleBarHeight: CGFloat = 28
    @EnvironmentObject var model: PlayerModel

    var body: some View {
        VStack(spacing: 0) {
            if model.settings.compact {
                CompactView()
            } else {
                PlayerView()
                if model.settings.showEq { EqView() }
                if model.settings.showPlaylist {
                    PlaylistView().frame(minHeight: 200, idealHeight: 300, maxHeight: .infinity)
                }
            }
        }
        .frame(width: 560)
        .background(Theme.window)
        // The hidden title bar still reserves its height above the content, and the window grows by it.
        // Pull the content up under the (transparent) title bar instead, so the window matches the design:
        // our header row shares the strip with the traffic lights.
        .padding(.top, -ContentView.titleBarHeight)
        .preferredColorScheme(.dark)
        .tint(Theme.accent)
        .sheet(item: $model.rekordboxHelp) { RekordboxHelpSheet(info: $0) }
        .sheet(item: $model.fileInfo) { FileInfoSheet(track: $0) }
    }
}

/// Figma frame "SOUNDBOY / Windowshade" (560 × 44).
struct CompactView: View {
    @EnvironmentObject var model: PlayerModel

    var body: some View {
        ZStack(alignment: .topLeading) {
            Color.clear
            LogoMark(size: 24).at(76, 10, 24, 24)
            MarqueeText(text: model.nowPlayingTitle, font: .system(size: 13, weight: .semibold), color: Theme.text)
                .at(108, 13, 184, 18)
            Text(model.state == .stopped ? "" : TimeFormat.clock(model.position))
                .font(Theme.mono(12)).foregroundStyle(Theme.accent)
                .frame(width: 52, height: 18, alignment: .trailing).offset(x: 296, y: 13)
                .onTapGesture { model.toggle(\.remainingTime) }
            IconButton(symbol: "backward.end.fill", size: 14, emphasis: true, help: "Previous") { model.previous() }.at(356, 11, 22, 22)
            IconButton(symbol: model.state == .playing ? "pause.fill" : "play.fill", size: 14, style: .primary,
                       help: "Play / Pause") { model.playPause() }.at(382, 10, 24, 24)
            IconButton(symbol: "forward.end.fill", size: 14, emphasis: true, help: "Next") { model.next() }.at(410, 11, 22, 22)
            HSlider(value: model.duration > 0 ? model.position / model.duration : 0, kind: .mini,
                    enabled: model.state != .stopped && model.duration > 0, onCommit: { model.seek(fraction: $0) })
                .at(440, 14, 48, 16)
            IconButton(symbol: "rectangle.expand.vertical", size: 14, style: .raised, help: "Full mode (⇧⌘M)") {
                model.toggle(\.compact)
            }.at(522, 11, 22, 22)
        }
        .frame(width: 560, height: 44)
    }
}

struct RekordboxHelpSheet: View {
    let info: RekordboxHelpInfo
    @EnvironmentObject var model: PlayerModel
    @Environment(\.dismiss) private var dismiss
    @State private var dontShow = false

    private var steps: [String] {
        var s: [String] = []
        if !info.sidebarEnabled { s.append("In rekordbox, open Preferences › View › Layout and tick “rekordbox xml”.") }
        s.append(info.configuredInRekordbox
                 ? "rekordbox is already set to read this file, so there's nothing to browse for."
                 : "Preferences › Advanced › Database › rekordbox xml › Imported Library: click Browse and pick the file below.")
        s.append("In rekordbox's sidebar open rekordbox xml › Playlists › SOUNDBOY (click the refresh icon next to “rekordbox xml” after each new export).")
        s.append("Right-click a playlist › Import Playlist, or drag tracks into your Collection. BPM and key come along; rekordbox analyzes beat grids and waveforms on import.")
        return s
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(info.result.map { "Exported to rekordbox: \($0.added) new, \($0.updated) updated, playlist “\($0.playlistName)”." }
                 ?? "SOUNDBOY exports to a rekordbox XML library.")
                .font(.system(size: 14, weight: .semibold))
                .fixedSize(horizontal: false, vertical: true)
            Text(info.result?.createdFile == false ? "To see it in rekordbox:" : "To see it in rekordbox (one-time setup):")
                .foregroundStyle(Theme.muted)
            ForEach(Array(steps.enumerated()), id: \.offset) { i, s in
                HStack(alignment: .top, spacing: 8) {
                    Text("\(i + 1).").foregroundStyle(Theme.accent).frame(width: 16, alignment: .trailing)
                    Text(s).fixedSize(horizontal: false, vertical: true)
                }
            }
            Text(info.xmlURL.path)
                .font(Theme.mono(11))
                .textSelection(.enabled)
                .padding(8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: 6).fill(Theme.window))
            Toggle("Don't show this after every export", isOn: $dontShow)
            HStack {
                Button("Copy Path") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(info.xmlURL.path, forType: .string)
                }
                Button("Show in Finder") {
                    let fm = FileManager.default
                    if fm.fileExists(atPath: info.xmlURL.path) { NSWorkspace.shared.activateFileViewerSelecting([info.xmlURL]) }
                    else { NSWorkspace.shared.open(info.xmlURL.deletingLastPathComponent()) }
                }
                Spacer()
                Button("OK") {
                    if dontShow { model.settings.rekordboxHelpShown = true; model.saveSettingsSoon() }
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .font(.system(size: 13))
        .padding(22)
        .frame(width: 520)
        .background(Theme.surface)
    }
}

struct FileInfoSheet: View {
    let track: Track
    @Environment(\.dismiss) private var dismiss

    private var rows: [(String, String)] {
        var r: [(String, String?)] = [
            ("Title", track.title), ("Artist", track.artist), ("Album", track.album),
            ("Year", track.year > 0 ? String(track.year) : nil), ("Genre", track.genre),
            ("Length", track.duration.map(TimeFormat.length)),
            ("Bitrate", track.bitrate > 0 ? "\(track.bitrate) kbps" : nil),
            ("Sample rate", track.sampleRate > 0 ? "\(track.sampleRate) Hz" : nil),
            ("Channels", track.channels == 0 ? nil : track.channels == 1 ? "Mono" : track.channels == 2 ? "Stereo" : "\(track.channels) channels"),
            ("BPM", track.displayBpm.map { String(format: "%.1f", $0) + (track.tagBpm != nil ? " (from tags)" : "") }),
            ("Key", track.key.map { "\($0) (\(track.camelot ?? "") Camelot)" }),
        ]
        if let size = (try? FileManager.default.attributesOfItem(atPath: track.url.path))?[.size] as? NSNumber {
            r.append(("File size", ByteCountFormatter.string(fromByteCount: size.int64Value, countStyle: .file)))
        }
        r.append(("Location", track.url.path))
        return r.compactMap { k, v in v.map { (k, $0) } }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("File Info").font(.system(size: 14, weight: .semibold))
            Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 14, verticalSpacing: 6) {
                ForEach(rows, id: \.0) { k, v in
                    GridRow {
                        Text(k).foregroundStyle(Theme.muted)
                        Text(v).textSelection(.enabled).lineLimit(3)
                    }
                }
            }
            HStack {
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([track.url]) }
                Spacer()
                Button("Close") { dismiss() }.keyboardShortcut(.defaultAction)
            }
        }
        .font(.system(size: 13))
        .padding(22)
        .frame(width: 520)
        .background(Theme.surface)
    }
}
