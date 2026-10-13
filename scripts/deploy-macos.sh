#!/usr/bin/env bash
# Local deploy script: build the macOS arm64 self-contained app, package it
# as the v2rayMini.app bundle and zip the bundle.
# NOTE: the run is gated on the v2rayMini menu-bar feature epic (E1) completing;
# this script must not be executed until that chain is done.
set -euo pipefail

# Derive the repo root from the script location, never from $PWD.
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

OUT_DIR="./dist/v2rayMini-osx-arm64"
ZIP_PATH="./dist/v2rayMini-osx-arm64.zip"

rm -rf dist
mkdir -p dist

# Exact upstream CI flags (see .github/workflows/build.yml).
# Fall back to the dotnet SDK container on hosts without a local dotnet SDK.
if command -v dotnet >/dev/null 2>&1; then
    dotnet publish ./v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj -c Release -r osx-arm64 -p:SelfContained=true -o "$OUT_DIR"
else
    docker run --rm -v "$REPO_ROOT":/src -v v2rayn-nuget:/root/.nuget -w /src \
        mcr.microsoft.com/dotnet/sdk:10.0 \
        dotnet publish ./v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj -c Release -r osx-arm64 -p:SelfContained=true -o "$OUT_DIR"

    # The SDK container runs as root; on Linux SDK-less hosts the dist tree would stay
    # root-owned afterwards. macOS hosts are the deploy target and skip this.
    if [[ "$(uname -s)" != "Darwin" ]]; then
        docker run --rm -v "$REPO_ROOT":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 chown -R "$(id -u):$(id -g)" dist
    fi
fi

# CI parity: strip pdb files from the output.
find "$OUT_DIR" -name '*.pdb' -delete

# Package the publish output as a double-clickable v2rayMini.app bundle
# (modeled on the upstream package-osx.sh).
APP_DIR="$REPO_ROOT/dist/v2rayMini.app"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$OUT_DIR/." "$APP_DIR/Contents/MacOS/"
cp "$APP_DIR/Contents/MacOS/v2rayN.icns" "$APP_DIR/Contents/Resources/AppIcon.icns"
printf '%s\n' "When this file exists, app will not store configs under this folder" > "$APP_DIR/Contents/MacOS/NotStoreConfigHere.txt"
chmod +x "$APP_DIR/Contents/MacOS/v2rayN"

BUNDLE_VERSION="$(sed -nE 's/.*<Version>([^<]+)<\/Version>.*/\1/p' v2rayN/Directory.Build.props | head -n1 || true)"
BUNDLE_VERSION="${BUNDLE_VERSION:-7.25.6}"

cat > "$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleLocalizations</key>
  <array>
    <string>zh-Hans</string>
    <string>zh-Hant</string>
    <string>en</string>
    <string>fa</string>
    <string>fr</string>
    <string>ru</string>
    <string>hu</string>
  </array>
  <key>CFBundleDisplayName</key>
  <string>v2rayMini</string>
  <key>CFBundleExecutable</key>
  <string>v2rayN</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIconName</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>com.zamotkin.v2rayMini</string>
  <key>CFBundleName</key>
  <string>v2rayMini</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${BUNDLE_VERSION}</string>
  <key>CSResourcesFileMapped</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>LSMinimumSystemVersion</key>
  <string>13.6</string>
</dict>
</plist>
EOF

# ditto preserves unix exec permissions (plain zip can strip them and break
# the app); on non-mac fall back to zip -r -y.
if [[ "$(uname -s)" == "Darwin" ]]; then
    ditto -c -k --keepParent "$APP_DIR" "$ZIP_PATH"
else
    (cd dist && zip -r -y v2rayMini-osx-arm64.zip v2rayMini.app)
fi

ABS_ZIP_PATH="$(cd "$(dirname "$ZIP_PATH")" && pwd)/$(basename "$ZIP_PATH")"
ABS_ZIP_SIZE="$(du -h "$ABS_ZIP_PATH" | cut -f1)"
echo "Artifact: $ABS_ZIP_PATH ($ABS_ZIP_SIZE)"
