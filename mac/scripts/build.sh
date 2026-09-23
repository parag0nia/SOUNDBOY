#!/bin/bash
# Builds SOUNDBOY.app (universal: Apple Silicon + Intel), signs it and packages a DMG.
#   mac/scripts/build.sh [version]
#
# Signing & notarization run only when these environment variables are set (GitHub secrets on CI):
#   MACOS_CERTIFICATE            base64 of your "Developer ID Application" .p12
#   MACOS_CERTIFICATE_PASSWORD   password of that .p12
#   APPLE_ID                     Apple ID email used for notarization
#   APPLE_TEAM_ID                10-character Team ID
#   APPLE_APP_PASSWORD           app-specific password (appleid.apple.com › Sign-In and Security)
# Without them the app is ad-hoc signed (runs after "Open Anyway" in System Settings).
set -euo pipefail

cd "$(dirname "$0")/.."
MAC_DIR="$PWD"
REPO_DIR="$(cd .. && pwd)"
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_DIR/SOUNDBOY/SOUNDBOY.csproj" | head -1)}"
BUILD_NUMBER="${GITHUB_RUN_NUMBER:-1}"
OUT="$MAC_DIR/build"
APP="$OUT/SOUNDBOY.app"

echo "==> SOUNDBOY $VERSION ($BUILD_NUMBER)"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "==> Compiling universal binary"
swift build -c release --arch arm64 --arch x86_64 --product SOUNDBOY
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/SOUNDBOY"
lipo -info "$BIN"

echo "==> Assembling app bundle"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/SOUNDBOY"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD_NUMBER/" Support/Info.plist > "$APP/Contents/Info.plist"
cp "$REPO_DIR/SOUNDBOY/Assets/logo.png" "$APP/Contents/Resources/logo.png"
cp "$REPO_DIR/SOUNDBOY/Assets/Dunno Sample Shout.mp3" "$APP/Contents/Resources/Dunno Sample Shout.mp3"
swift scripts/make-icon.swift "$REPO_DIR/SOUNDBOY/Assets/logo.png" "$OUT/AppIcon.iconset"
iconutil -c icns "$OUT/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"
plutil -lint "$APP/Contents/Info.plist"

SIGNED=0
if [[ -n "${MACOS_CERTIFICATE:-}" ]]; then
    echo "==> Importing Developer ID certificate"
    KEYCHAIN="$OUT/build.keychain-db"
    KEYCHAIN_PASSWORD="$(uuidgen)"
    security create-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
    security set-keychain-settings -lut 21600 "$KEYCHAIN"
    security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
    echo "$MACOS_CERTIFICATE" | base64 --decode > "$OUT/cert.p12"
    security import "$OUT/cert.p12" -k "$KEYCHAIN" -P "$MACOS_CERTIFICATE_PASSWORD" -T /usr/bin/codesign
    rm -f "$OUT/cert.p12"
    security set-key-partition-list -S apple-tool:,apple: -s -k "$KEYCHAIN_PASSWORD" "$KEYCHAIN" >/dev/null
    security list-keychains -d user -s "$KEYCHAIN" $(security list-keychains -d user | tr -d '"')
    IDENTITY="$(security find-identity -v -p codesigning "$KEYCHAIN" | sed -n 's/.*"\(Developer ID Application:.*\)"/\1/p' | head -1)"
    [[ -n "$IDENTITY" ]] || { echo "No 'Developer ID Application' identity in the certificate"; exit 1; }
    echo "==> Signing as: $IDENTITY"
    codesign --force --options runtime --timestamp --entitlements Support/SOUNDBOY.entitlements --sign "$IDENTITY" "$APP"
    SIGNED=1
else
    echo "==> No signing certificate configured: ad-hoc signing"
    codesign --force --sign - "$APP"
fi
codesign --verify --strict --verbose=2 "$APP"

notarize() {
    xcrun notarytool submit "$1" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait --timeout 30m
}

if [[ $SIGNED == 1 && -n "${APPLE_ID:-}" ]]; then
    echo "==> Notarizing the app"
    ditto -c -k --keepParent "$APP" "$OUT/SOUNDBOY-notarize.zip"
    notarize "$OUT/SOUNDBOY-notarize.zip"
    xcrun stapler staple "$APP"
    rm -f "$OUT/SOUNDBOY-notarize.zip"
fi

echo "==> Building DMG"
DMG="$OUT/SOUNDBOY-$VERSION-mac.dmg"
STAGE="$OUT/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "SOUNDBOY $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"

if [[ $SIGNED == 1 ]]; then
    codesign --force --timestamp --sign "$IDENTITY" "$DMG"
    if [[ -n "${APPLE_ID:-}" ]]; then
        echo "==> Notarizing the DMG"
        notarize "$DMG"
        xcrun stapler staple "$DMG"
        spctl --assess --type open --context context:primary-signature --verbose=2 "$DMG" || true
    fi
    security delete-keychain "$KEYCHAIN" || true
fi

echo "==> Done: $DMG"
ls -la "$OUT"
