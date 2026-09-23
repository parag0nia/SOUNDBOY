# SOUNDBOY

Desktop music player for Windows (Winamp-style features, custom dark UI).

| Folder / file | What |
|---|---|
| `SOUNDBOY/` | The app (C# / .NET 9 WinForms). See `SOUNDBOY/README.md`. |
| `Installer/` | Setup wizard + uninstaller (.NET Framework 4.8, no runtime needed on target PCs). |
| `build-kit.ps1` | Builds the installation kit → `SOUNDBOY-<version>-Kit/` (Setup.exe, portable ZIP, README, checksums). |

Design: Figma file "DunnoMusic — Player UI" (https://www.figma.com/design/fNshE8iSY04Ih6DvSGJ8Y1).

## Common tasks

```
dotnet run --project SOUNDBOY                       # run from source
powershell -ExecutionPolicy Bypass -File build-kit.ps1   # build the installer kit
```

Set `SOUNDBOY_PROFILE=test` to run a second, isolated copy (own settings folder and
single-instance lock) without touching your normal SOUNDBOY.
