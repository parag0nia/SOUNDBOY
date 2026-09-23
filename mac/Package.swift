// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "SOUNDBOY",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "SOUNDBOY", targets: ["SOUNDBOY"]),
    ],
    targets: [
        // Platform logic shared by the app and the tests: tracks, playlists, EQ presets,
        // BPM/key analysis, analysis cache and the Rekordbox XML export.
        .target(name: "SoundboyCore", path: "Sources/SoundboyCore"),
        // The SwiftUI app: audio engine, player model and views.
        .executableTarget(name: "SOUNDBOY", dependencies: ["SoundboyCore"], path: "Sources/SOUNDBOY"),
        .testTarget(name: "SoundboyCoreTests", dependencies: ["SoundboyCore"], path: "Tests/SoundboyCoreTests"),
    ]
)
