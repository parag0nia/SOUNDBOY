# SOUNDBOY for macOS

Native SwiftUI app (macOS 13 Ventura or later, universal: Apple Silicon + Intel) with the same design and
features as the Windows version: playback via AVAudioEngine, 10-band EQ with presets and Winamp `.eqf`
import/export, spectrum/oscilloscope visualizer, playlist editor, BPM & key detection, right-click
**Export to Rekordbox**, media keys / Control Center "Now Playing", compact mode and the bundled sample.

Formats: MP3, AAC/M4A, ALAC, WAV, AIFF, FLAC (what Core Audio decodes; OGG and WMA aren't supported on macOS).

## Layout

| Path | What |
|------|------|
| `Sources/SoundboyCore/` | Tracks, playlists (M3U/PLS), EQ presets, BPM/key analysis, analysis cache, Rekordbox XML export |
| `Sources/SOUNDBOY/` | The app: `AudioEngine`, `PlayerModel` (state + commands), `NowPlaying` (media keys), SwiftUI views |
| `Tests/SoundboyCoreTests/` | Tempo/key accuracy on synthesized tracks, Rekordbox XML, `.eqf`, playlists |
| `Support/` | `Info.plist` template and entitlements |
| `scripts/build.sh` | Builds the universal `.app`, generates the icon, signs, notarizes and makes the `.dmg` |

## Building

On a Mac with Xcode 15+:

```
cd mac
swift test                 # core tests
bash scripts/build.sh      # → build/SOUNDBOY.app and build/SOUNDBOY-<version>-mac.dmg
```

GitHub Actions (`.github/workflows/mac.yml`) does the same on every push that touches `mac/`, uploads the
DMG plus UI snapshots as a build artifact, and attaches the DMG to the release when a `v*` tag is pushed.

## Signing & notarization (one-time setup)

Without these secrets the DMG is ad-hoc signed: macOS blocks the first launch and users must click
**System Settings › Privacy & Security › Open Anyway**. With them, SOUNDBOY opens without warnings.

1. **Developer ID certificate** — in Xcode › Settings › Accounts › Manage Certificates, click **+** and
   choose **Developer ID Application** (requires a paid Apple Developer membership).
2. **Export it** — in Keychain Access › My Certificates, right-click
   *Developer ID Application: …* › Export › save as `.p12` with a password.
3. **App-specific password** — at [account.apple.com](https://account.apple.com) › Sign-In and Security ›
   App-Specific Passwords, create one named "SOUNDBOY notarization".
4. **Team ID** — [developer.apple.com/account](https://developer.apple.com/account) › Membership details.
5. **Add the secrets** to the GitHub repo (Settings › Secrets and variables › Actions), or from Terminal:

   ```
   base64 -i DeveloperID.p12 | gh secret set MACOS_CERTIFICATE --repo parag0nia/SOUNDBOY
   gh secret set MACOS_CERTIFICATE_PASSWORD --repo parag0nia/SOUNDBOY   # the .p12 password
   gh secret set APPLE_ID --repo parag0nia/SOUNDBOY                     # your Apple ID email
   gh secret set APPLE_TEAM_ID --repo parag0nia/SOUNDBOY                # e.g. AB12CD34EF
   gh secret set APPLE_APP_PASSWORD --repo parag0nia/SOUNDBOY           # the app-specific password
   ```

The next build then signs with the hardened runtime, notarizes the app and the DMG with Apple, and staples
the tickets.
