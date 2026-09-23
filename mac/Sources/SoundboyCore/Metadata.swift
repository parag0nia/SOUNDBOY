import AVFoundation

/// Tags, stream properties and artwork via AVFoundation.
public enum Metadata {
    public static func load(_ t: Track) async {
        let asset = AVURLAsset(url: t.url)
        guard FileManager.default.fileExists(atPath: t.url.path) else {
            t.missing = true
            t.infoLoaded = true
            return
        }
        do {
            let (duration, common, formats) = try await asset.load(.duration, .commonMetadata, .availableMetadataFormats)
            let secs = CMTimeGetSeconds(duration)
            if secs.isFinite, secs > 0 { t.duration = secs }

            var title: String?, artist: String?, album: String?
            for item in common {
                guard let key = item.commonKey else { continue }
                switch key {
                case .commonKeyTitle: title = try? await item.load(.stringValue)
                case .commonKeyArtist: artist = try? await item.load(.stringValue)
                case .commonKeyAlbumName: album = try? await item.load(.stringValue)
                default: break
                }
            }

            var year = 0, genre: String?, tagBpm: Double?
            for format in formats {
                let items = (try? await asset.loadMetadata(for: format)) ?? []
                for item in items {
                    guard let id = item.identifier else { continue }
                    switch id {
                    case .id3MetadataBeatsPerMinute, .iTunesMetadataBeatsPerMin:
                        if let s = try? await item.load(.stringValue), let v = Double(s.trimmingCharacters(in: .whitespaces)), v > 0 { tagBpm = v }
                        else if let n = try? await item.load(.numberValue), n.doubleValue > 0 { tagBpm = n.doubleValue }
                    case .id3MetadataYear, .id3MetadataRecordingTime, .iTunesMetadataReleaseDate, .commonIdentifierCreationDate:
                        if year == 0, let s = try? await item.load(.stringValue), let y = Int(s.prefix(4)) { year = y }
                    case .id3MetadataContentType, .iTunesMetadataUserGenre, .quickTimeMetadataGenre:
                        if genre == nil { genre = try? await item.load(.stringValue) }
                    default: break
                    }
                }
            }

            if !t.fixedTitle, let title, !title.trimmingCharacters(in: .whitespaces).isEmpty {
                t.title = title.trimmingCharacters(in: .whitespaces)
                t.artist = artist?.trimmingCharacters(in: .whitespaces)
            }
            if !t.fixedTitle {
                t.album = album
                t.genre = genre
                t.year = year
            }
            t.tagBpm = tagBpm

            if let audio = try await asset.loadTracks(withMediaType: .audio).first {
                let (rate, descriptions) = try await audio.load(.estimatedDataRate, .formatDescriptions)
                if rate > 0 { t.bitrate = Int((rate / 1000).rounded()) }
                if let d = descriptions.first, let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(d)?.pointee {
                    t.sampleRate = Int(asbd.mSampleRate)
                    t.channels = Int(asbd.mChannelsPerFrame)
                }
            }
            t.missing = false
        } catch {
            t.missing = !FileManager.default.fileExists(atPath: t.url.path)
        }
        t.infoLoaded = true
    }

    /// Embedded cover art, if any.
    public static func artwork(for url: URL) async -> Data? {
        let asset = AVURLAsset(url: url)
        guard let common = try? await asset.load(.commonMetadata) else { return nil }
        let items = AVMetadataItem.metadataItems(from: common, filteredByIdentifier: .commonIdentifierArtwork)
        guard let first = items.first else { return nil }
        return try? await first.load(.dataValue)
    }
}
