#!/bin/bash
# Builds "Ezkiel Notch.app" (universal: Apple Silicon + Intel), then a DMG and a ZIP in dist/.
#   ./scripts/build_app.sh            # universal
#   ARCHS="arm64" ./scripts/build_app.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

APP_NAME="Ezkiel Notch"
EXEC="EzkielNotch"
DIST="$ROOT/dist"
APP="$DIST/$APP_NAME.app"
VERSION=$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' Resources/Info.plist)
ARCHS="${ARCHS:-arm64 x86_64}"

ARCH_FLAGS=()
for a in $ARCHS; do ARCH_FLAGS+=(--arch "$a"); done

echo "→ Building $APP_NAME $VERSION for: $ARCHS"
swift build -c release "${ARCH_FLAGS[@]}"
BIN_DIR="$(swift build -c release "${ARCH_FLAGS[@]}" --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/$EXEC" "$APP/Contents/MacOS/$EXEC"
cp Resources/Info.plist "$APP/Contents/Info.plist"
printf 'APPLEZKN' > "$APP/Contents/PkgInfo"

echo "→ Rendering lion icon"
ICONSET="$(mktemp -d)/AppIcon.iconset"
"$APP/Contents/MacOS/$EXEC" --render-icon "$ICONSET"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

plutil -lint "$APP/Contents/Info.plist"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
lipo -info "$APP/Contents/MacOS/$EXEC"

SUFFIX="macOS-universal"
[[ "$ARCHS" == "arm64" ]] && SUFFIX="macOS-arm64"
[[ "$ARCHS" == "x86_64" ]] && SUFFIX="macOS-intel"

ZIP="$DIST/EzkielNotch-$VERSION-$SUFFIX.zip"
DMG="$DIST/EzkielNotch-$VERSION-$SUFFIX.dmg"
rm -f "$ZIP" "$DMG"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"

STAGING="$(mktemp -d)"
ditto "$APP" "$STAGING/$APP_NAME.app"
ln -s /Applications "$STAGING/Applications"
hdiutil create -volname "Ezkiel Notch $VERSION" -srcfolder "$STAGING" -ov -format UDZO "$DMG" >/dev/null
hdiutil verify "$DMG" >/dev/null
rm -rf "$STAGING"

# The in-app updater only installs a DMG whose SHA-256 matches this file from the same release.
(cd "$DIST" && shasum -a 256 "$(basename "$DMG")" "$(basename "$ZIP")" > SHA256SUMS.txt)

echo "✓ $APP"
echo "✓ $ZIP"
echo "✓ $DMG"
echo "✓ $DIST/SHA256SUMS.txt"
