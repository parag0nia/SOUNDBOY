// Builds AppIcon.iconset from the SOUNDBOY head logo: macOS-style rounded square (Big Sur grid) in the
// app's dark palette with the head centered. Usage: swift make-icon.swift <logo.png> <out.iconset>
import AppKit

let args = CommandLine.arguments
guard args.count == 3, let logo = NSImage(contentsOfFile: args[1]) else {
    FileHandle.standardError.write("usage: make-icon.swift <logo.png> <out.iconset>\n".data(using: .utf8)!)
    exit(1)
}
let out = URL(fileURLWithPath: args[2])
try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)

func render(_ px: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    let s = CGFloat(px)
    // Apple's icon grid: 824/1024 body with ~185/1024 corner radius, centered.
    let inset = s * 100 / 1024
    let body = NSRect(x: inset, y: inset, width: s - 2 * inset, height: s - 2 * inset)
    let path = NSBezierPath(roundedRect: body, xRadius: s * 185 / 1024, yRadius: s * 185 / 1024)
    NSGraphicsContext.saveGraphicsState()
    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.35)
    shadow.shadowBlurRadius = s * 10 / 1024
    shadow.shadowOffset = NSSize(width: 0, height: -s * 6 / 1024)
    shadow.set()
    NSColor(red: 0.06, green: 0.06, blue: 0.08, alpha: 1).setFill()
    path.fill()
    NSGraphicsContext.restoreGraphicsState()
    let gradient = NSGradient(starting: NSColor(red: 0.20, green: 0.20, blue: 0.25, alpha: 1),
                              ending: NSColor(red: 0.06, green: 0.06, blue: 0.08, alpha: 1))!
    gradient.draw(in: path, angle: -60)
    // Head, centered at ~72% of the body.
    let target = body.width * 0.72
    let k = min(target / logo.size.width, target / logo.size.height)
    let w = logo.size.width * k, h = logo.size.height * k
    logo.draw(in: NSRect(x: body.midX - w / 2, y: body.midY - h / 2, width: w, height: h),
              from: .zero, operation: .sourceOver, fraction: 1)
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

for base in [16, 32, 128, 256, 512] {
    try! render(base).write(to: out.appendingPathComponent("icon_\(base)x\(base).png"))
    try! render(base * 2).write(to: out.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}
print("wrote \(out.path)")
