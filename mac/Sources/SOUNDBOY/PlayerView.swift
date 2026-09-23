import SoundboyCore
import SwiftUI

/// Figma frame "SOUNDBOY / Main window" › Player (560 × 252). Coordinates match the design 1:1;
/// the window buttons are replaced by macOS's own traffic lights.
struct PlayerView: View {
    @EnvironmentObject var model: PlayerModel

    var body: some View {
        ZStack(alignment: .topLeading) {
            Color.clear
            header
            card.at(12, 44, 536, 128)
            progress.at(0, 176, 560, 24)
            controls.at(0, 200, 560, 52)
        }
        .frame(width: 560, height: 252)
        .dropDestination(for: URL.self) { urls, _ in
            // Winamp behavior: dropping on the player replaces the playlist and plays.
            model.open(urls, play: true, replace: true)
            return true
        }
    }

    // MARK: header (traffic lights sit at the left)

    private var header: some View {
        ZStack(alignment: .topLeading) {
            HStack(spacing: 8) {
                LogoMark(size: 24)
                Text("SOUNDBOY").font(.system(size: 14, weight: .bold)).foregroundStyle(Theme.text)
            }
            .at(78, 8, 160, 24)
            PillButton(title: "EQ", active: model.settings.showEq, font: .system(size: 10, weight: .bold), help: "Equalizer (⌥⌘E)") {
                model.toggle(\.showEq)
            }.at(418, 9, 34, 22)
            PillButton(title: "PL", active: model.settings.showPlaylist, font: .system(size: 10, weight: .bold), help: "Playlist (⌥⌘P)") {
                model.toggle(\.showPlaylist)
            }.at(456, 9, 34, 22)
            IconButton(symbol: "rectangle.compress.vertical", size: 14, style: .raised, help: "Compact mode (⇧⌘M)") {
                model.toggle(\.compact)
            }.at(494, 9, 22, 22)
            MainMenuButton().at(522, 9, 22, 22)
        }
    }

    // MARK: now playing card

    private var card: some View {
        ZStack(alignment: .topLeading) {
            RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Theme.surface)
            Artwork(image: model.artwork)
                .at(12, 12, 104, 104)
                .onTapGesture { model.showFileInfo(nil) }
                .help("File info")
            MarqueeText(text: model.nowPlayingTitle, font: .system(size: 18, weight: .semibold), color: Theme.text)
                .at(132, 12, 392, 26)
            Group {
                if let t = model.transient {
                    Text(t).foregroundStyle(Theme.accent)
                } else {
                    Text(model.nowPlayingSubtitle).foregroundStyle(Theme.muted)
                }
            }
            .font(.system(size: 13))
            .lineLimit(1)
            .frame(width: 392, height: 18, alignment: .leading)
            .offset(x: 132, y: 40)
            ChipsRow(chips: model.chips).at(132, 64, 392, 18)
            VisualizerView().at(132, 88, 392, 28)
        }
    }

    // MARK: progress

    private var progress: some View {
        let dur = model.duration
        let pos = model.state == .stopped ? 0 : model.position
        let right = dur <= 0 ? "--:--" : model.settings.remainingTime ? "-" + TimeFormat.clock(dur - pos) : TimeFormat.length(dur)
        return ZStack(alignment: .topLeading) {
            TimelineView(.periodic(from: .now, by: 0.5)) { ctx in
                let blinkOff = model.state == .paused && Int(ctx.date.timeIntervalSinceReferenceDate * 2) % 2 == 1
                Text(TimeFormat.clock(pos))
                    .font(Theme.mono(12))
                    .foregroundStyle(model.state == .stopped ? Theme.muted : Theme.text)
                    .opacity(blinkOff ? 0 : 1)
            }
            .frame(width: 44, height: 16, alignment: .leading)
            .offset(x: 16, y: 4)
            .onTapGesture { model.toggle(\.remainingTime) }
            HSlider(value: dur > 0 ? pos / dur : 0, kind: .seek, enabled: model.state != .stopped && dur > 0,
                    onChanging: { v in model.showTransient("Seek to \(TimeFormat.clock(dur * v)) / \(TimeFormat.clock(dur))") },
                    onCommit: { v in model.seek(fraction: v) })
                .at(64, 0, 432, 24)
                .help("Seek (← →: 5 s)")
            Text(right)
                .font(Theme.mono(12)).foregroundStyle(Theme.muted)
                .frame(width: 44, height: 16, alignment: .trailing)
                .offset(x: 500, y: 4)
                .onTapGesture { model.toggle(\.remainingTime) }
        }
    }

    // MARK: transport

    private var controls: some View {
        ZStack(alignment: .topLeading) {
            IconButton(symbol: "shuffle", active: model.settings.shuffle, help: "Shuffle (S)") { model.toggleShuffle() }.at(16, 10, 32, 32)
            IconButton(symbol: model.settings.repeatMode == .one ? "repeat.1" : "repeat",
                       active: model.settings.repeatMode != .off, help: "Repeat: off / all / one (R)") { model.cycleRepeat() }.at(52, 10, 32, 32)
            IconButton(symbol: "stop.fill", size: 16, help: "Stop (V)") { model.stop() }.at(172, 10, 32, 32)
            IconButton(symbol: "backward.end.fill", size: 20, emphasis: true, help: "Previous (Z)") { model.previous() }.at(212, 8, 36, 36)
            IconButton(symbol: model.state == .playing ? "pause.fill" : "play.fill", size: 24, style: .primary,
                       help: "Play / Pause (Space)") { model.playPause() }.at(256, 2, 48, 48)
            IconButton(symbol: "forward.end.fill", size: 20, emphasis: true, help: "Next (B)") { model.next() }.at(312, 8, 36, 36)
            IconButton(symbol: "eject.fill", size: 16, help: "Open file(s) (⌘O)") { model.openFilesPanel(replace: true) }.at(356, 10, 32, 32)
            IconButton(symbol: model.settings.volume <= 0.001 ? "speaker.slash.fill" : "speaker.wave.2.fill", size: 15,
                       help: "Mute") { model.toggleMute() }.at(408, 2, 28, 24)
            HSlider(value: model.settings.volume, kind: .volume,
                    onChanging: { v in model.setVolume(v); model.showTransient("Volume \(Int(v * 100))%") })
                .at(440, 6, 104, 16)
                .help("Volume (⌘↑ ⌘↓)")
            Text("BAL").font(.system(size: 9, weight: .bold)).foregroundStyle(Theme.dim).at(408, 26, 28, 12)
            HSlider(value: model.settings.balance / 2 + 0.5, kind: .balance, resetValue: 0.5,
                    onChanging: { v in
                        let b = v * 2 - 1
                        model.setBalance(b)
                        model.showTransient(abs(b) < 0.07 ? "Balance: center" : "Balance: \(Int(abs(b) * 100))% \(b < 0 ? "left" : "right")")
                    })
                .at(440, 24, 104, 16)
                .help("Balance (double-click to center)")
        }
    }
}

struct ChipsRow: View {
    var chips: [(text: String, musical: Bool)]
    var body: some View {
        HStack(spacing: 4) {
            ForEach(Array(chips.enumerated()), id: \.offset) { _, chip in
                Text(chip.text)
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(chip.musical ? Theme.accent : Theme.muted)
                    .padding(.horizontal, 9)
                    .frame(height: 18)
                    .background(Capsule().fill(chip.musical ? Theme.accent.opacity(0.11) : Theme.raised))
                    .fixedSize()
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .clipped()
    }
}

struct VisualizerView: View {
    @EnvironmentObject var model: PlayerModel
    @ObservedObject var vis: VisModel = PlayerModel.shared.vis

    var body: some View {
        Canvas { ctx, size in
            switch model.settings.vis {
            case .spectrum:
                let bw: CGFloat = 6, gap: CGFloat = 3
                let gradient = Gradient(stops: [
                    .init(color: Theme.visHigh, location: 0), .init(color: Theme.visMid, location: 0.3),
                    .init(color: Theme.accent, location: 0.7), .init(color: Theme.accent, location: 1),
                ])
                let shading = GraphicsContext.Shading.linearGradient(gradient, startPoint: .zero, endPoint: CGPoint(x: 0, y: size.height))
                for i in 0..<VisModel.barCount {
                    let x = CGFloat(i) * (bw + gap)
                    let h = CGFloat(vis.bars[i]) * size.height
                    if h < 3 {
                        ctx.fill(Path(roundedRect: CGRect(x: x, y: size.height - 3, width: bw, height: 3), cornerRadius: 1), with: .color(Theme.raised))
                    } else {
                        ctx.fill(Path(roundedRect: CGRect(x: x, y: size.height - h, width: bw, height: h), cornerRadius: 1), with: shading)
                    }
                    if model.settings.showPeaks, vis.peaks[i] > 0.04 {
                        let py = max(0, size.height - CGFloat(vis.peaks[i]) * size.height - 3)
                        ctx.fill(Path(CGRect(x: x, y: py, width: bw, height: 1.5)), with: .color(Theme.text.opacity(0.66)))
                    }
                }
            case .oscilloscope:
                var p = Path()
                let s = vis.scope
                for i in 0..<Int(size.width / 2) {
                    let v = CGFloat(min(max(s[i * s.count / Int(size.width / 2)], -1), 1))
                    let pt = CGPoint(x: CGFloat(i) * 2, y: size.height / 2 - v * (size.height / 2 - 1))
                    if i == 0 { p.move(to: pt) } else { p.addLine(to: pt) }
                }
                ctx.stroke(p, with: .color(Theme.accent), lineWidth: 1.5)
            case .off:
                ctx.fill(Path(CGRect(x: 0, y: size.height - 2, width: size.width, height: 2)), with: .color(Theme.raised))
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { model.cycleVis() }
        .help("Click to change the visualizer")
    }
}

/// The "⋯" menu in the header.
struct MainMenuButton: View {
    @EnvironmentObject var model: PlayerModel
    @State private var hover = false

    var body: some View {
        Menu {
            Button("Open Files…") { model.openFilesPanel(replace: true) }
            Button("Add Files or Folder…") { model.openFilesPanel(replace: false) }
            Button("Open Playlist…") { model.openPlaylistFile() }
            Button("Save Playlist…") { model.savePlaylistFile() }
            Divider()
            Button("Export Playlist to Rekordbox") { model.exportToRekordbox(model.tracks) }.disabled(model.tracks.isEmpty || model.rekordboxBusy)
            Button("Rekordbox Export File…") { model.chooseRekordboxFile() }
            Button("Rekordbox Setup Help…") { model.showRekordboxHelp(nil) }
            Divider()
            Picker("Visualizer", selection: Binding(get: { model.settings.vis }, set: { model.settings.vis = $0; model.saveSettingsSoon() })) {
                Text("Spectrum").tag(VisMode.spectrum)
                Text("Oscilloscope").tag(VisMode.oscilloscope)
                Text("Off").tag(VisMode.off)
            }
            Toggle("Show Peaks", isOn: Binding(get: { model.settings.showPeaks }, set: { _ in model.toggle(\.showPeaks) }))
            Toggle("Show Remaining Time", isOn: Binding(get: { model.settings.remainingTime }, set: { _ in model.toggle(\.remainingTime) }))
            Toggle("Always on Top", isOn: Binding(get: { model.settings.alwaysOnTop }, set: { _ in model.toggle(\.alwaysOnTop) }))
            Toggle("Stop After Current Track", isOn: Binding(get: { model.stopAfterCurrent }, set: { model.stopAfterCurrent = $0 }))
        } label: {
            Image(systemName: "ellipsis").font(.system(size: 12, weight: .bold))
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .foregroundStyle(hover ? Theme.text : Theme.muted)
        .frame(width: 22, height: 22)
        .background(RoundedRectangle(cornerRadius: 6).fill(hover ? Theme.selected : Theme.raised))
        .onHover { hover = $0 }
        .help("Menu")
    }
}
