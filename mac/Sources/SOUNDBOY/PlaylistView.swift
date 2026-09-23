import SoundboyCore
import SwiftUI

/// Figma frame "SOUNDBOY / Main window" › Playlist. Grows with the window.
struct PlaylistView: View {
    @EnvironmentObject var model: PlayerModel
    @State private var searching = false
    @FocusState private var searchFocused: Bool

    private var filtered: [(offset: Int, element: Track)] {
        let all = Array(model.tracks.enumerated())
        let words = model.search.lowercased().split(separator: " ")
        guard !words.isEmpty else { return all }
        return all.filter { _, t in
            let hay = (t.displayTitle + " " + t.url.path).lowercased()
            return words.allSatisfy { hay.contains($0) }
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            header.frame(height: 40)
            list
                .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Theme.surface))
                .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                .padding(.horizontal, 12)
            footer.frame(height: 52)
        }
        .frame(width: 560)
        .dropDestination(for: URL.self) { urls, _ in
            model.open(urls, play: false, replace: false)
            return true
        }
    }

    private var header: some View {
        HStack(spacing: 8) {
            Text("Playlist").font(.system(size: 13, weight: .semibold)).foregroundStyle(Theme.text)
            if searching {
                TextField("Search playlist", text: $model.search)
                    .textFieldStyle(.plain)
                    .font(.system(size: 12))
                    .padding(.horizontal, 8)
                    .frame(height: 22)
                    .background(RoundedRectangle(cornerRadius: 6).fill(Theme.raised))
                    .focused($searchFocused)
                    .onSubmit { if let first = filtered.first { model.playTrack(first.element) } }
                    .onExitCommand { closeSearch() }
            } else {
                Text(meta).font(.system(size: 12)).foregroundStyle(Theme.muted).lineLimit(1)
            }
            Spacer()
            IconButton(symbol: searching ? "xmark.circle" : "magnifyingglass", size: 14, style: .raised, help: "Jump to file (J)") {
                if searching { closeSearch() } else { searching = true; searchFocused = true }
            }.frame(width: 22, height: 22)
            IconButton(symbol: "xmark", size: 14, style: .raised, help: "Hide playlist (⌥⌘P)") { model.toggle(\.showPlaylist) }
                .frame(width: 22, height: 22)
        }
        .padding(.leading, 16)
        .padding(.trailing, 16)
        .onReceive(NotificationCenter.default.publisher(for: .soundboyJumpToFile)) { _ in
            searching = true
            searchFocused = true
        }
    }

    private func closeSearch() {
        searching = false
        model.search = ""
    }

    private var meta: String {
        let n = model.tracks.count
        let unknown = model.tracks.contains { $0.duration == nil }
        var s = "\(n) \(n == 1 ? "track" : "tracks") · \(TimeFormat.clock(model.totalDuration))\(unknown && n > 0 ? "+" : "")"
        if model.selection.count > 1 { s += " · \(model.selection.count) selected" }
        return s
    }

    private var list: some View {
        List(selection: $model.selection) {
            ForEach(filtered, id: \.element.id) { item in
                TrackRow(track: item.element, index: item.offset, isCurrent: item.element === model.current,
                         playing: model.state == .playing)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 0, leading: 8, bottom: 0, trailing: 8))
            }
            .onMove { source, dest in
                if model.search.isEmpty { model.move(from: source, to: dest) }
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .environment(\.defaultMinListRowHeight, 32)
        .contextMenu(forSelectionType: Track.ID.self) { ids in
            contextMenu(ids)
        } primaryAction: { ids in
            if let t = model.tracks.first(where: { ids.contains($0.id) }) { model.playTrack(t) }
        }
        .onDeleteCommand { model.removeSelected() }
        .overlay {
            if model.tracks.isEmpty {
                Text("Drop music here, or click + Add").font(.system(size: 13)).foregroundStyle(Theme.dim)
            }
        }
    }

    @ViewBuilder private func contextMenu(_ ids: Set<Track.ID>) -> some View {
        let picked = model.tracks.filter { ids.contains($0.id) }
        if picked.isEmpty {
            Button("Export Playlist to Rekordbox") { model.exportToRekordbox(model.tracks) }
                .disabled(model.tracks.isEmpty || model.rekordboxBusy)
            Button("Add Files or Folder…") { model.openFilesPanel(replace: false) }
        } else {
            Button("Play") { model.playTrack(picked[0]) }
            Button("File Info…") { model.showFileInfo(picked[0]) }
            Button("Show in Finder") { model.showInFinder(picked) }
            Divider()
            Button("Export \(picked.count) Track\(picked.count == 1 ? "" : "s") to Rekordbox") { model.exportToRekordbox(picked) }
                .disabled(model.rekordboxBusy)
            Divider()
            Button("Remove") { model.tracks.removeAll { ids.contains($0.id) }; model.selection.subtract(ids) }
        }
    }

    private var footer: some View {
        HStack(spacing: 6) {
            PillMenu(title: "+ Add", emphasis: true) {
                Button("Add Files or Folder…") { model.openFilesPanel(replace: false) }
                Button("Open Playlist…") { model.openPlaylistFile() }
            }.frame(width: 60, height: 28)
            PillMenu(title: "Remove") {
                Button("Remove Selected") { model.removeSelected() }.disabled(model.selection.isEmpty)
                Button("Crop (Keep Selected)") { model.crop() }.disabled(model.selection.isEmpty)
                Button("Clear Playlist") { model.clearPlaylist() }
                Divider()
                Button("Remove Missing Files") { model.removeMissing() }
                Button("Remove Duplicates") { model.removeDuplicates() }
            }.frame(width: 70, height: 28)
            PillMenu(title: "Select") {
                Button("Select All") { model.selectAll() }
                Button("Select None") { model.selection.removeAll() }
                Button("Invert Selection") { model.invertSelection() }
            }.frame(width: 62, height: 28)
            PillMenu(title: "Sort") {
                Button("By Title") { model.sort { $0.displayTitle.lowercased() } }
                Button("By Artist") { model.sort { ($0.artist ?? $0.displayTitle).lowercased() } }
                Button("By Album") { model.sort { ($0.album ?? "").lowercased() } }
                Button("By File Name") { model.sort { $0.url.lastPathComponent.lowercased() } }
                Button("By BPM") { model.sort { $0.displayBpm ?? 0 } }
                Button("By Length") { model.sort { $0.duration ?? 0 } }
                Divider()
                Button("Reverse") { model.reverse() }
                Button("Randomize") { model.randomize() }
            }.frame(width: 52, height: 28)
            PillMenu(title: "Lists") {
                Button("New Playlist") { model.clearPlaylist() }
                Button("Open Playlist…") { model.openPlaylistFile() }
                Button("Save Playlist…") { model.savePlaylistFile() }
                Divider()
                Button("Export Playlist to Rekordbox") { model.exportToRekordbox(model.tracks) }.disabled(model.tracks.isEmpty || model.rekordboxBusy)
                Button("Rekordbox Export File…") { model.chooseRekordboxFile() }
                Button("Rekordbox Setup Help…") { model.showRekordboxHelp(nil) }
            }.frame(width: 54, height: 28)
            Spacer()
            if model.state != .stopped {
                Text("\(TimeFormat.clock(model.position)) / \(model.duration > 0 ? TimeFormat.length(model.duration) : "--:--")")
                    .font(Theme.mono(12)).foregroundStyle(Theme.muted)
            }
        }
        .padding(.horizontal, 12)
    }
}

struct TrackRow: View {
    let track: Track
    let index: Int
    let isCurrent: Bool
    let playing: Bool

    var body: some View {
        HStack(spacing: 0) {
            Group {
                if isCurrent {
                    PlayingIndicator(animating: playing)
                } else {
                    Text("\(index + 1)").font(Theme.mono(12)).foregroundStyle(Theme.dim)
                }
            }
            .frame(width: 40, alignment: .leading)
            Text(track.displayTitle)
                .font(.system(size: 13, weight: isCurrent ? .semibold : .regular))
                .foregroundStyle(isCurrent ? Theme.accent : track.missing ? Theme.visHigh : Theme.text)
                .lineLimit(1)
            Spacer(minLength: 12)
            Text(track.duration.map(TimeFormat.length) ?? "")
                .font(Theme.mono(12))
                .foregroundStyle(isCurrent ? Theme.accent : Theme.muted)
        }
        .frame(height: 32)
        .padding(.horizontal, 8)
        .contentShape(Rectangle())
    }
}

/// Three bouncing bars (Figma: PlayingIndicator).
struct PlayingIndicator: View {
    var animating: Bool
    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 20, paused: !animating)) { ctx in
            let hs = Self.heights(ctx.date, animating: animating)
            HStack(alignment: .bottom, spacing: 2) {
                ForEach(0..<3, id: \.self) { i in
                    RoundedRectangle(cornerRadius: 1).fill(Theme.accent).frame(width: 3, height: hs[i])
                }
            }
            .frame(height: 12, alignment: .bottom)
        }
    }

    static func heights(_ date: Date, animating: Bool) -> [CGFloat] {
        guard animating else { return [8, 12, 6] }
        let ph: Double = date.timeIntervalSinceReferenceDate * 5.5
        let a: Double = 6 + 5 * abs(sin(ph))
        let b: Double = 6 + 6 * abs(sin(ph + 1.3))
        let c: Double = 5 + 5 * abs(sin(ph + 2.1))
        return [CGFloat(a), CGFloat(b), CGFloat(c)]
    }
}

extension Notification.Name {
    static let soundboyJumpToFile = Notification.Name("soundboyJumpToFile")
}
