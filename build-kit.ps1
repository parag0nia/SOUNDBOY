# Builds the SOUNDBOY installation kit.
#   powershell -ExecutionPolicy Bypass -File build-kit.ps1
# Output: SOUNDBOY-<version>-Kit\  (Setup.exe, portable ZIP, README.txt, SHA256SUMS.txt)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = ([xml](Get-Content "$root\SOUNDBOY\SOUNDBOY.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$work = Join-Path $root 'kit-build'
$payload = Join-Path $work 'payload'
$kit = Join-Path $root "SOUNDBOY-$version-Kit"

Remove-Item $work, $kit -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $payload, $kit | Out-Null

function Invoke-Dotnet([string[]]$a) {
    & dotnet @a | Where-Object { $_ -match ' error |warning NETSDK' } | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($a[0]) failed ($LASTEXITCODE)" }
}

Write-Host "1/4  Publishing SOUNDBOY $version (self-contained: no .NET needed on the target PC)..."
Invoke-Dotnet @('publish', "$root\SOUNDBOY\SOUNDBOY.csproj", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none', '-o', $payload)

Write-Host "2/4  Building the uninstaller..."
Invoke-Dotnet @('build', "$root\Installer\Uninstall\Uninstall.csproj", '-c', 'Release', '-o', "$work\uninstall")
Copy-Item "$work\uninstall\uninstall.exe" $payload

Write-Host "3/4  Building the setup (embeds the app + uninstaller)..."
Invoke-Dotnet @('build', "$root\Installer\Setup\Setup.csproj", '-c', 'Release', "-p:PayloadDir=$payload", '-o', "$work\setup")

Write-Host "4/4  Assembling the kit..."
$setup = Join-Path $kit "SOUNDBOY-$version-Setup.exe"
Copy-Item "$work\setup\SOUNDBOY-Setup.exe" $setup

$portableDir = Join-Path $work 'portable\SOUNDBOY'
New-Item -ItemType Directory -Force $portableDir | Out-Null
Copy-Item "$payload\SOUNDBOY.exe" $portableDir
@"
SOUNDBOY $version (portable)

Run SOUNDBOY.exe from any folder. Nothing is installed; settings and playlists are stored in
%APPDATA%\SOUNDBOY. To remove it, delete this folder (and %APPDATA%\SOUNDBOY if you want).
"@ | Set-Content (Join-Path $portableDir 'README.txt') -Encoding UTF8
$zip = Join-Path $kit "SOUNDBOY-$version-Portable.zip"
Compress-Archive -Path $portableDir -DestinationPath $zip -Force

Copy-Item "$root\Installer\README.txt" (Join-Path $kit 'README.txt')
(Get-Content (Join-Path $kit 'README.txt') -Raw).Replace('{VERSION}', $version) | Set-Content (Join-Path $kit 'README.txt') -Encoding UTF8 -NoNewline

$sums = Get-ChildItem $kit -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$sums | Set-Content (Join-Path $kit 'SHA256SUMS.txt') -Encoding ASCII

Remove-Item $work -Recurse -Force
Write-Host ""
Write-Host "Kit ready: $kit"
Get-ChildItem $kit | ForEach-Object { "  {0,-34} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
