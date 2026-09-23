import AppKit
import SoundboyCore
import SwiftUI

@main
struct SoundboyApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var model = PlayerModel.shared

    var body: some Scene {
        Window("SOUNDBOY", id: "main") {
            ContentView().environmentObject(model)
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
        .defaultPosition(.center)
        .commands { SoundboyCommands(model: model) }
    }
}

struct SoundboyCommands: Commands {
    @ObservedObject var model: PlayerModel

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("Open Files…") { model.openFilesPanel(replace: true) }.keyboardShortcut("o")
            Button("Add Files or Folder…") { model.openFilesPanel(replace: false) }.keyboardShortcut("o", modifiers: [.command, .shift])
            Button("Open Playlist…") { model.openPlaylistFile() }
            Button("Save Playlist…") { model.savePlaylistFile() }.keyboardShortcut("s")
            Divider()
            Button("Export Selection to Rekordbox") {
                model.exportToRekordbox(model.selectedTracks.isEmpty ? model.tracks : model.selectedTracks)
            }
            .keyboardShortcut("e")
            .disabled(model.tracks.isEmpty || model.rekordboxBusy)
        }
        CommandMenu("Playback") {
            Button(model.state == .playing ? "Pause" : "Play") { model.playPause() }
            Button("Stop") { model.stop() }.keyboardShortcut(".")
            Button("Next") { model.next() }.keyboardShortcut(.rightArrow, modifiers: [.command, .option])
            Button("Previous") { model.previous() }.keyboardShortcut(.leftArrow, modifiers: [.command, .option])
            Divider()
            Button("Volume Up") { model.volume(by: 0.05) }.keyboardShortcut(.upArrow)
            Button("Volume Down") { model.volume(by: -0.05) }.keyboardShortcut(.downArrow)
            Button("Mute") { model.toggleMute() }.keyboardShortcut(.downArrow, modifiers: [.command, .option])
            Divider()
            Toggle("Shuffle", isOn: Binding(get: { model.settings.shuffle }, set: { _ in model.toggleShuffle() }))
            Button("Repeat: \(["Off", "Playlist", "Current Track"][model.settings.repeatMode.rawValue])") { model.cycleRepeat() }
            Toggle("Stop After Current Track", isOn: Binding(get: { model.stopAfterCurrent }, set: { model.stopAfterCurrent = $0 }))
        }
        CommandGroup(after: .sidebar) {
            Toggle("Equalizer", isOn: Binding(get: { model.settings.showEq }, set: { _ in model.toggle(\.showEq) }))
                .keyboardShortcut("e", modifiers: [.command, .option])
            Toggle("Playlist", isOn: Binding(get: { model.settings.showPlaylist }, set: { _ in model.toggle(\.showPlaylist) }))
                .keyboardShortcut("p", modifiers: [.command, .option])
            Toggle("Compact Mode", isOn: Binding(get: { model.settings.compact }, set: { _ in model.toggle(\.compact) }))
                .keyboardShortcut("m", modifiers: [.command, .shift])
            Toggle("Always on Top", isOn: Binding(get: { model.settings.alwaysOnTop }, set: { _ in model.toggle(\.alwaysOnTop) }))
            Divider()
        }
        CommandGroup(replacing: .help) {
            Button("SOUNDBOY Keyboard Shortcuts") { AppDelegate.showShortcuts() }
            Button("Rekordbox Setup Help…") { model.showRekordboxHelp(nil) }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var keyMonitor: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.appearance = NSAppearance(named: .darkAqua)
        DispatchQueue.main.async { self.configureWindows() }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            Self.handleKey(event) ? nil : event
        }
        if let i = CommandLine.arguments.firstIndex(of: "--snapshot"), i + 1 < CommandLine.arguments.count {
            Snapshot.run(outputDirectory: URL(fileURLWithPath: CommandLine.arguments[i + 1]))
        }
    }

    func configureWindows() {
        for w in NSApp.windows where !(w is NSPanel) {
            w.isMovableByWindowBackground = true
            w.backgroundColor = Theme.nsWindow
            w.titlebarAppearsTransparent = true
            w.level = PlayerModel.shared.settings.alwaysOnTop ? .floating : .normal
        }
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        PlayerModel.shared.open(urls, play: true, replace: false)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        PlayerModel.shared.saveState()
    }

    /// Winamp-style single keys. Ignored while typing in a text field.
    static func handleKey(_ event: NSEvent) -> Bool {
        if NSApp.keyWindow?.firstResponder is NSText { return false }
        let mods = event.modifierFlags.intersection([.command, .option, .control])
        guard mods.isEmpty, let ch = event.charactersIgnoringModifiers?.lowercased() else { return false }
        let m = PlayerModel.shared
        switch ch {
        case "z": m.previous()
        case "x": m.playPressed()
        case "c": m.state == .playing ? m.pause() : m.playPressed()
        case "v": m.stop()
        case "b": m.next()
        case " ": m.playPause()
        case "s": m.toggleShuffle()
        case "r": m.cycleRepeat()
        case "j":
            if !m.settings.showPlaylist { m.toggle(\.showPlaylist) }
            NotificationCenter.default.post(name: .soundboyJumpToFile, object: nil)
        default:
            switch event.keyCode {
            case 123: m.seek(by: -5)  // ←
            case 124: m.seek(by: 5)   // →
            default: return false
            }
        }
        return true
    }

    static func showShortcuts() {
        let a = NSAlert()
        a.messageText = "SOUNDBOY \(PlayerModel.version)"
        a.informativeText = """
        Z / X / C / V / B — Previous / Play / Pause / Stop / Next
        Space — Play / Pause
        ← / → — Seek 5 seconds
        ⌘↑ / ⌘↓ — Volume
        S / R — Shuffle / Repeat
        J — Jump to file (search the playlist)
        ⌘O — Open files · ⇧⌘O — Add files or folder
        ⌘S — Save playlist · ⌘E — Export to Rekordbox
        ⌥⌘E / ⌥⌘P — Equalizer / Playlist · ⇧⌘M — Compact mode
        Delete — Remove selected tracks · Return / double-click — Play

        Media keys, AirPods and Control Center control SOUNDBOY while it's the active player.
        Click the time to toggle total/remaining, the visualizer to change modes, and the artwork for file info.
        """
        a.runModal()
    }
}

/// `SOUNDBOY --snapshot <dir>`: renders the UI with demo data into PNGs and quits (used on CI to review the design).
enum Snapshot {
    static func run(outputDirectory dir: URL) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let m = PlayerModel.shared
        m.loadDemo()
        m.settings.showEq = true
        m.settings.showPlaylist = true
        m.settings.compact = false
        let steps: [(Double, () -> Void)] = [
            (2.0, { capture("main.png") }),
            (0.2, { m.settings.compact = true }),
            (1.5, { capture("compact.png") }),
            (0.2, { m.settings.compact = false; m.settings.showEq = false }),
            (1.5, { capture("no-eq.png"); m.settings.showEq = true }),
            (0.5, {
                m.rekordboxHelp = RekordboxHelpInfo(
                    xmlURL: URL(fileURLWithPath: "/Users/you/Library/Pioneer/rekordbox/rekordbox.xml"),
                    result: nil, configuredInRekordbox: true, sidebarEnabled: false)
            }),
            (1.5, { capture("rekordbox-help.png", sheet: true) }),
            (0.5, { NSApp.terminate(nil) }),
        ]
        var t = 0.0
        for (delay, action) in steps {
            t += delay
            DispatchQueue.main.asyncAfter(deadline: .now() + t, execute: action)
        }

        func capture(_ name: String, sheet: Bool = false) {
            guard var w = NSApp.windows.first(where: { $0.isVisible && !($0 is NSPanel) }) else { return }
            if sheet, let s = w.attachedSheet { w = s }
            let url = dir.appendingPathComponent(name)
            if let cg = CGWindowListCreateImage(.null, .optionIncludingWindow, CGWindowID(w.windowNumber), [.boundsIgnoreFraming, .bestResolution]) {
                try? NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:])?.write(to: url)
            } else if let view = w.contentView, let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
                view.cacheDisplay(in: view.bounds, to: rep)
                try? rep.representation(using: .png, properties: [:])?.write(to: url)
            }
        }
    }
}
