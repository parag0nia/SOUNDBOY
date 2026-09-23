import AppKit
import SwiftUI

/// SOUNDBOY design tokens: same values as the "SOUNDBOY Colors" variables in the Figma file.
enum Theme {
    static let window = Color(hex: 0x0F0F14)     // bg/window
    static let surface = Color(hex: 0x17171F)    // bg/surface
    static let raised = Color(hex: 0x22222D)     // bg/raised
    static let selected = Color(hex: 0x2C2C3C)   // bg/selected
    static let text = Color(hex: 0xF4F4F8)       // text/primary
    static let muted = Color(hex: 0x8B8BA3)      // text/muted
    static let dim = Color(hex: 0x55556B)        // text/dim
    static let accent = Color(hex: 0xC8FF3D)     // accent/primary
    static let ink = Color(hex: 0x0F0F14)        // accent/ink
    static let visMid = Color(hex: 0xFFD23D)     // vis/mid
    static let visHigh = Color(hex: 0xFF6B3D)    // vis/high

    static let nsWindow = NSColor(red: 0x0F / 255.0, green: 0x0F / 255.0, blue: 0x14 / 255.0, alpha: 1)

    static func mono(_ size: CGFloat) -> Font { .system(size: size, weight: .medium, design: .monospaced) }

    static var logo: NSImage? = Bundle.main.url(forResource: "logo", withExtension: "png").flatMap(NSImage.init(contentsOf:))
}

extension Color {
    init(hex: UInt32, opacity: Double = 1) {
        self.init(.sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255, opacity: opacity)
    }
}

/// Places a view at Figma coordinates inside a ZStack(alignment: .topLeading).
extension View {
    func at(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> some View {
        frame(width: w, height: h).offset(x: x, y: y)
    }
}

// MARK: - logo & artwork

struct LogoMark: View {
    var size: CGFloat
    var body: some View {
        if let img = Theme.logo {
            Image(nsImage: img).resizable().interpolation(.high).aspectRatio(contentMode: .fit).frame(width: size, height: size)
        } else {
            Circle().fill(Theme.muted).frame(width: size, height: size)
        }
    }
}

struct Artwork: View {
    var image: NSImage?
    var body: some View {
        ZStack {
            if let image {
                Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fill)
            } else {
                LinearGradient(colors: [Color(hex: 0x30303B), Theme.window], startPoint: .topLeading, endPoint: .bottomTrailing)
                LogoMark(size: 70)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
    }
}

// MARK: - buttons

enum PillStyle { case ghost, raised, pill, primary }

struct IconButton: View {
    var symbol: String
    var size: CGFloat = 18
    var style: PillStyle = .ghost
    var active = false
    var emphasis = false
    var help: String = ""
    var action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            ZStack {
                switch style {
                case .primary: Circle().fill(hover ? Color(hex: 0xD8FF70) : Theme.accent)
                case .raised: RoundedRectangle(cornerRadius: 6).fill(hover ? Theme.selected : Theme.raised)
                case .pill: Capsule().fill(hover ? Theme.selected : Theme.raised)
                case .ghost: RoundedRectangle(cornerRadius: 8).fill(hover ? Theme.raised : .clear)
                }
                Image(systemName: symbol)
                    .font(.system(size: size * 0.8, weight: .semibold))
                    .foregroundStyle(style == .primary ? Theme.ink : active ? Theme.accent : (hover || emphasis) ? Theme.text : Theme.muted)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .help(help)
    }
}

struct PillButton: View {
    var title: String
    var active = false
    var emphasis = false
    var font: Font = .system(size: 11, weight: .semibold)
    var help: String = ""
    var action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(font)
                .foregroundStyle(active ? Theme.accent : (hover || emphasis) ? Theme.text : Theme.muted)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Capsule().fill(hover ? Theme.selected : Theme.raised))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .help(help)
    }
}

/// A menu that looks like a PillButton.
struct PillMenu<Content: View>: View {
    var title: String
    var emphasis = false
    @ViewBuilder var content: () -> Content
    @State private var hover = false

    var body: some View {
        Menu(content: content) {
            Text(title).font(.system(size: 11, weight: .semibold))
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .foregroundStyle((hover || emphasis) ? Theme.text : Theme.muted)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Capsule().fill(hover ? Theme.selected : Theme.raised))
        .contentShape(Capsule())
        .onHover { hover = $0 }
    }
}

struct Switch: View {
    var isOn: Bool
    var action: () -> Void
    var body: some View {
        Button(action: action) {
            ZStack(alignment: isOn ? .trailing : .leading) {
                Capsule().fill(isOn ? Theme.accent : Theme.raised)
                Circle().fill(isOn ? Theme.ink : Theme.muted).frame(width: 16, height: 16).padding(2)
            }
        }
        .buttonStyle(.plain)
        .animation(.easeOut(duration: 0.12), value: isOn)
    }
}

// MARK: - sliders

enum SliderKind { case seek, volume, balance, mini }

/// Slim horizontal slider (Figma: SeekTrack / VolumeTrack / BalanceTrack).
struct HSlider: View {
    var value: Double              // 0...1
    var kind: SliderKind
    var enabled = true
    var resetValue: Double? = nil
    var onChanging: (Double) -> Void = { _ in }
    var onCommit: (Double) -> Void = { _ in }
    @State private var drag: Double?
    @State private var hover = false

    var body: some View {
        GeometryReader { g in
            let w = g.size.width
            let v = drag ?? value
            let x = CGFloat(v) * w
            let hot = hover || drag != nil
            ZStack(alignment: .leading) {
                Capsule().fill(Theme.raised).frame(height: 4)
                if enabled {
                    if kind == .balance {
                        Rectangle().fill(Theme.dim).frame(width: 2, height: 8).offset(x: w / 2 - 1)
                        Capsule().fill(Theme.accent).frame(width: abs(x - w / 2), height: 4).offset(x: min(x, w / 2))
                    } else {
                        Capsule().fill(Theme.accent).frame(width: max(0, x), height: 4)
                    }
                    if kind != .mini {
                        let d: CGFloat = (kind == .seek ? 12 : 10) + (hot ? 2 : 0)
                        Circle().fill(kind == .balance && !hot ? Theme.muted : Theme.text)
                            .frame(width: d, height: d)
                            .offset(x: x - d / 2)
                    }
                }
            }
            .frame(maxHeight: .infinity)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0)
                .onChanged { g in
                    guard enabled else { return }
                    let nv = min(max(Double(g.location.x / w), 0), 1)
                    drag = nv
                    onChanging(nv)
                }
                .onEnded { _ in
                    guard enabled, let d = drag else { return }
                    onCommit(d)
                    drag = nil
                })
            .simultaneousGesture(TapGesture(count: 2).onEnded {
                if let r = resetValue, enabled { onChanging(r); onCommit(r) }
            })
            .onHover { hover = $0 }
        }
    }
}

/// Vertical EQ slider (Figma: Band/* columns).
struct VSlider: View {
    var value: Double              // 0...1, 1 = top
    var onChanging: (Double) -> Void
    @State private var drag: Double?
    @State private var hover = false

    var body: some View {
        GeometryReader { g in
            let h = g.size.height
            let v = drag ?? value
            let y = CGFloat(1 - v) * h
            ZStack(alignment: .top) {
                Capsule().fill(Theme.raised).frame(width: 4, height: h)
                Capsule().fill(Theme.accent).frame(width: 4, height: abs(y - h / 2)).offset(y: min(y, h / 2))
                Capsule().fill(hover || drag != nil ? Color.white : Theme.text).frame(width: 20, height: 10).offset(y: y - 5)
            }
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0)
                .onChanged { g in
                    let nv = min(max(1 - Double(g.location.y / h), 0), 1)
                    drag = nv
                    onChanging(nv)
                }
                .onEnded { _ in drag = nil })
            .simultaneousGesture(TapGesture(count: 2).onEnded { onChanging(0.5) })
            .onHover { hover = $0 }
        }
    }
}

// MARK: - marquee

/// Title text that scrolls when it doesn't fit (Winamp heritage).
struct MarqueeText: View {
    var text: String
    var font: Font
    var color: Color
    @State private var textWidth: CGFloat = 0
    private let gap: CGFloat = 48

    var body: some View {
        GeometryReader { g in
            let fits = textWidth <= g.size.width
            TimelineView(.animation(minimumInterval: 1.0 / 30, paused: fits)) { ctx in
                let cycle = Double(textWidth + gap) / 32 + 2
                let t = ctx.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: max(cycle, 0.1))
                let offset = fits ? 0 : CGFloat(max(0, t - 2)) * 32
                HStack(spacing: gap) {
                    label
                    if !fits { label }
                }
                .offset(x: -offset)
            }
            .frame(width: g.size.width, height: g.size.height, alignment: .leading)
            .clipped()
        }
    }

    private var label: some View {
        Text(text).font(font).foregroundStyle(color).lineLimit(1).fixedSize()
            .background(GeometryReader { p in Color.clear.onAppear { textWidth = p.size.width }.onChange(of: text) { _ in textWidth = p.size.width } })
    }
}
