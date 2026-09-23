# SOUNDBOY

A desktop music player for Windows with Winamp-class features, written in C# / .NET 9 WinForms.
All UI is custom-painted to match the Figma file **"SOUNDBOY — Player UI"**
(https://www.figma.com/design/fNshE8iSY04Ih6DvSGJ8Y1). Audio uses NAudio, tags use TagLibSharp.

## Build & run

```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ../SOUNDBOY-release
```

Needs the .NET 9 Desktop Runtime (add `--self-contained true` for a standalone exe) and Windows 10 1809+.

## Design ↔ code

| Figma | Code |
|-------|------|
| Variables "SOUNDBOY Colors" | `UI/Skin.cs` color tokens (same names in comments) |
| Main window › Player, Windowshade | `UI/PlayerPanel.cs` |
| Main window › Equalizer | `UI/EqPanel.cs` |
| Main window › Playlist | `UI/PlaylistPanel.cs` |
| Icons (24×24 SVG) | `Skin.DrawIcon` |

Frames are 1:1 with the app in logical pixels at 100% zoom.

## Notable behavior

- **Media keys**: registered with Windows System Media Transport Controls (`Audio/MediaSession.cs`), so
  Play/Pause, Next, Previous, Stop, Rewind/Fast-forward keys and headset buttons work globally, and the
  player appears in the Windows media flyout with title, artist and cover art.
- **Bundled sample**: `Assets/Dunno Sample Shout.mp3` is embedded, extracted to
  `%APPDATA%\SOUNDBOY\Samples` and re-added to the top of the playlist on every launch.
- Settings and the auto-saved playlist live in `%APPDATA%\SOUNDBOY`.
