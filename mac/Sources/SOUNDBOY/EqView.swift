import SoundboyCore
import SwiftUI

/// Figma frame "SOUNDBOY / Main window" › Equalizer (560 × 228).
struct EqView: View {
    @EnvironmentObject var model: PlayerModel
    private let trackTop: CGFloat = 20, trackH: CGFloat = 128 // inside the card
    private let preampX: CGFloat = 30, firstBandX: CGFloat = 104, bandStep: CGFloat = 44

    var body: some View {
        ZStack(alignment: .topLeading) {
            Color.clear
            Text("Equalizer").font(.system(size: 13, weight: .semibold)).foregroundStyle(Theme.text).at(16, 10, 90, 20)
            Switch(isOn: model.settings.eqEnabled) { model.toggleEqEnabled() }.at(100, 10, 34, 20).help("Turn equalizer on/off")
            if !model.settings.eqEnabled {
                Text("Off").font(.system(size: 12)).foregroundStyle(Theme.dim).at(142, 10, 40, 20)
            }
            PillButton(title: "Reset", help: "Reset all sliders to 0 dB") {
                model.applyEq([Float](repeating: 0, count: Equalizer.bandCount), preamp: 0)
            }.at(358, 8, 60, 24)
            PillMenu(title: "Presets ▾", emphasis: true) { presetMenu }.at(424, 8, 90, 24)
            IconButton(symbol: "xmark", size: 14, style: .raised, help: "Hide equalizer (⌥⌘E)") { model.toggle(\.showEq) }.at(522, 9, 22, 22)
            card.at(12, 40, 536, 180)
        }
        .frame(width: 560, height: 228)
    }

    @ViewBuilder private var presetMenu: some View {
        Menu("Load Preset") {
            ForEach(EqPresets.builtIn, id: \.name) { p in
                Button(p.name) { model.applyEq(p.bands, preamp: p.preamp) }
            }
            if !model.settings.userPresets.isEmpty {
                Divider()
                ForEach(model.settings.userPresets.keys.sorted(), id: \.self) { name in
                    Button(name) {
                        let v = model.settings.userPresets[name] ?? []
                        model.applyEq(Array(v.prefix(10)), preamp: v.count > 10 ? v[10] : 0)
                    }
                }
            }
        }
        Button("Save Preset…") { model.savePresetPrompt() }
        Menu("Delete Preset") {
            ForEach(model.settings.userPresets.keys.sorted(), id: \.self) { name in
                Button(name) { model.settings.userPresets[name] = nil; model.saveSettingsSoon() }
            }
        }
        .disabled(model.settings.userPresets.isEmpty)
        Divider()
        Button("Import Winamp Presets (.eqf)…") { model.importEqf() }
        Button("Export My Presets (.eqf)…") { model.exportEqf() }.disabled(model.settings.userPresets.isEmpty)
        Divider()
        Button("Reset to Flat") { model.applyEq([Float](repeating: 0, count: Equalizer.bandCount), preamp: 0) }
    }

    private func value(_ db: Float) -> Double { Double(db / (2 * Equalizer.maxDb)) + 0.5 }
    private func db(_ v: Double) -> Float { Float((v * 2 - 1)) * Equalizer.maxDb }

    private var card: some View {
        ZStack(alignment: .topLeading) {
            RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Theme.surface)
            // dB scale
            ForEach([("+12", trackTop), ("0", trackTop + trackH / 2), ("−12", trackTop + trackH)], id: \.0) { label, y in
                Text(label).font(.system(size: 10, weight: .medium)).foregroundStyle(Theme.dim)
                    .frame(width: 28, height: 12, alignment: .trailing).offset(x: 40, y: y - 6)
            }
            Rectangle().fill(Theme.raised).at(76, trackTop, 1, trackH)
            curve
            slider(x: preampX, label: "PRE", value: value(model.settings.eqPreamp)) { model.setPreamp(db($0)) }
            ForEach(0..<Equalizer.bandCount, id: \.self) { i in
                slider(x: firstBandX + CGFloat(i) * bandStep, label: Equalizer.labels[i], value: value(model.settings.eqBands[i])) {
                    model.setBand(i, db($0))
                }
            }
        }
    }

    private func slider(x: CGFloat, label: String, value: Double, onChange: @escaping (Double) -> Void) -> some View {
        ZStack(alignment: .topLeading) {
            VSlider(value: value, onChanging: onChange).at(x - 14, trackTop, 28, trackH)
            Text(label).font(.system(size: 10, weight: .medium)).foregroundStyle(Theme.muted)
                .frame(width: 44, height: 14).offset(x: x - 22, y: trackTop + trackH + 8)
        }
    }

    /// Response curve behind the band thumbs (Figma: ResponseCurve).
    private var curve: some View {
        Canvas { ctx, _ in
            let cy = trackTop + trackH / 2
            let pts = (0..<Equalizer.bandCount).map { i in
                CGPoint(x: firstBandX + CGFloat(i) * bandStep, y: cy - CGFloat(model.settings.eqBands[i] / Equalizer.maxDb) * trackH / 2)
            }
            var p = Path()
            p.move(to: pts[0])
            for i in 1..<pts.count {
                let a = pts[i - 1], b = pts[i], mx = (a.x + b.x) / 2
                p.addCurve(to: b, control1: CGPoint(x: mx, y: a.y), control2: CGPoint(x: mx, y: b.y))
            }
            ctx.stroke(p, with: .color(Theme.accent.opacity(model.settings.eqEnabled ? 0.35 : 0.14)), lineWidth: 2)
        }
        .allowsHitTesting(false)
    }
}
