import AppKit
import MediaPlayer
import SoundboyCore

/// Media keys, AirPods/headset buttons, Control Center and the menu-bar "Now Playing" widget.
final class NowPlaying {
    private weak var model: PlayerModel?

    func install(_ model: PlayerModel) {
        self.model = model
        let c = MPRemoteCommandCenter.shared()
        c.playCommand.addTarget { [weak self] _ in self?.run { $0.playPressed() } ?? .commandFailed }
        c.pauseCommand.addTarget { [weak self] _ in self?.run { $0.pause() } ?? .commandFailed }
        c.togglePlayPauseCommand.addTarget { [weak self] _ in self?.run { $0.playPause() } ?? .commandFailed }
        c.stopCommand.addTarget { [weak self] _ in self?.run { $0.stop() } ?? .commandFailed }
        c.nextTrackCommand.addTarget { [weak self] _ in self?.run { $0.next() } ?? .commandFailed }
        c.previousTrackCommand.addTarget { [weak self] _ in self?.run { $0.previous() } ?? .commandFailed }
        c.skipForwardCommand.preferredIntervals = [10]
        c.skipBackwardCommand.preferredIntervals = [10]
        c.skipForwardCommand.addTarget { [weak self] _ in self?.run { $0.seek(by: 10) } ?? .commandFailed }
        c.skipBackwardCommand.addTarget { [weak self] _ in self?.run { $0.seek(by: -10) } ?? .commandFailed }
        c.changePlaybackPositionCommand.addTarget { [weak self] event in
            guard let e = event as? MPChangePlaybackPositionCommandEvent else { return .commandFailed }
            return self?.run { $0.seek(to: e.positionTime) } ?? .commandFailed
        }
    }

    private func run(_ action: @escaping (PlayerModel) -> Void) -> MPRemoteCommandHandlerStatus {
        guard let model else { return .commandFailed }
        if Thread.isMainThread { action(model) } else { DispatchQueue.main.async { action(model) } }
        return .success
    }

    func update(_ model: PlayerModel) {
        let center = MPNowPlayingInfoCenter.default()
        guard let t = model.current else {
            center.nowPlayingInfo = nil
            center.playbackState = .stopped
            return
        }
        var info: [String: Any] = [
            MPMediaItemPropertyTitle: t.displayTitleOnly,
            MPMediaItemPropertyArtist: t.artist ?? "",
            MPMediaItemPropertyAlbumTitle: t.album ?? "",
            MPMediaItemPropertyPlaybackDuration: model.engine.duration,
            MPNowPlayingInfoPropertyElapsedPlaybackTime: model.engine.position,
            MPNowPlayingInfoPropertyPlaybackRate: model.engine.state == .playing ? 1.0 : 0.0,
            MPNowPlayingInfoPropertyMediaType: MPNowPlayingInfoMediaType.audio.rawValue,
        ]
        let image = model.artwork ?? NSApp.applicationIconImage
        if let image {
            info[MPMediaItemPropertyArtwork] = MPMediaItemArtwork(boundsSize: image.size) { _ in image }
        }
        center.nowPlayingInfo = info
        switch model.engine.state {
        case .playing: center.playbackState = .playing
        case .paused: center.playbackState = .paused
        case .stopped: center.playbackState = .stopped
        }
    }
}
