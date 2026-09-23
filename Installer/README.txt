SOUNDBOY {VERSION} - installation kit
=====================================

What's in this folder
---------------------
  SOUNDBOY-{VERSION}-Setup.exe     Installer (recommended)
  SOUNDBOY-{VERSION}-Portable.zip  Portable copy, no installation
  SHA256SUMS.txt                   Checksums to verify the downloads
  README.txt                       This file

Requirements
------------
  Windows 10 (version 1809 or later) or Windows 11, 64-bit.
  Nothing else: the .NET runtime is built into the app.

Installing
----------
  1. Double-click SOUNDBOY-{VERSION}-Setup.exe.
  2. Choose the install location (default: %LOCALAPPDATA%\Programs\SOUNDBOY) and options:
       - Create a desktop shortcut
       - Add SOUNDBOY to "Open with" for music files (does not change your default player)
  3. Click Install, then Finish.

  It installs for the current user only, so no administrator rights or UAC prompt are needed.
  A Start menu shortcut is always created. Running Setup again updates an existing installation
  and keeps your playlist, settings, EQ presets and BPM/key cache.

  "Windows protected your PC": the installer isn't code-signed, so SmartScreen may show this on
  first run. Click "More info" > "Run anyway". You can verify the file with SHA256SUMS.txt:
      Get-FileHash .\SOUNDBOY-{VERSION}-Setup.exe -Algorithm SHA256

Silent install (IT / scripting)
-------------------------------
  SOUNDBOY-{VERSION}-Setup.exe /S [/D=<folder>] [/NODESKTOP] [/NOOPENWITH] [/LAUNCH]
  Exit code 0 = success, 1 = failure.

Uninstalling
------------
  Settings > Apps > Installed apps > SOUNDBOY > Uninstall
  (or run uninstall.exe in the install folder).
  You'll be asked whether to also delete your settings and playlists.
  Silent: uninstall.exe /S   (keeps user data)   or   uninstall.exe /S /PURGE   (deletes it)

Where things live
-----------------
  App:                     %LOCALAPPDATA%\Programs\SOUNDBOY\SOUNDBOY.exe
  Settings / playlist:     %APPDATA%\SOUNDBOY
  Start menu shortcut:     Start > SOUNDBOY
