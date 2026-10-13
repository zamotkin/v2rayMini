#!/usr/bin/env bash
# Local deploy script: build the macOS arm64 self-contained app, package it
# as the v2rayMini.app bundle and zip the bundle. Then install the xray core
# into the v2rayMini data dir and refresh /Applications/v2rayMini.app so
# Connect works out of the box.
# NOTE: the run is gated on the v2rayMini menu-bar feature epic (E1) completing;
# this script must not be executed until that chain is done.
set -euo pipefail

# Derive the repo root from the script location, never from $PWD.
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

# Abort if v2rayMini is running: deploying must never kill or clobber a live
# app (rm -rf below would pull the bundle out from under the running process).
abort_if_v2raymini_running() {
    if pgrep -f "/Applications/v2rayMini.app" >/dev/null 2>&1 || pgrep -x v2rayN >/dev/null 2>&1; then
        echo "ERROR: v2rayMini appears to be running. Close it first, then re-run $0; aborting." >&2
        exit 1
    fi
}
# Fail fast, before the expensive build.
abort_if_v2raymini_running

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

# --- Install the xray core into the v2rayMini data dir (latest 2dust/v2rayN-core-bin) ---
# xray only: v2rayMini runs a single core; geo files are downloaded by the app on demand.
CORE_REPO="2dust/v2rayN-core-bin"
CORE_DIR="$HOME/Library/Application Support/v2rayMini/bin/xray"
mkdir -p "$CORE_DIR"

CORE_TMP="$(mktemp -d "${TMPDIR:-/tmp}/v2rayMini-core.XXXXXX")"
trap 'rm -rf "$CORE_TMP"' EXIT

# Discover the latest macOS arm64 core archive via gh; do not hardcode blindly.
# The repo currently ships cores as plain files on master (no GitHub releases);
# prefer a matching release asset when one appears, else fall back to master.
CORE_ARCHIVE_URL=""
if gh release view --repo "$CORE_REPO" --json assets >/dev/null 2>&1; then
    CORE_ASSET="$(gh release view --repo "$CORE_REPO" --json assets --jq '[.assets[].name | select(test("(?i)(osx|macos)-arm64.*\\.zip"))][0] // empty')"
    if [[ -n "$CORE_ASSET" ]]; then
        CORE_TAG="$(gh release view --repo "$CORE_REPO" --json tagName --jq .tagName)"
        CORE_ARCHIVE_URL="https://github.com/$CORE_REPO/releases/download/$CORE_TAG/$CORE_ASSET"
    fi
fi
if [[ -z "$CORE_ARCHIVE_URL" ]]; then
    CORE_ARCHIVE_URL="$(gh api "repos/$CORE_REPO/contents/v2rayN-macos-arm64.zip?ref=master" --jq .download_url)"
fi
CORE_ARCHIVE_NAME="$(basename "$CORE_ARCHIVE_URL")"
echo "Core bin archive: $CORE_ARCHIVE_NAME ($CORE_ARCHIVE_URL)"

# Download to temp and verify the binary runs there; only then install in place.
curl -fSL --retry 3 -o "$CORE_TMP/$CORE_ARCHIVE_NAME" "$CORE_ARCHIVE_URL"
unzip -q "$CORE_TMP/$CORE_ARCHIVE_NAME" -d "$CORE_TMP/unpacked"
XRAY_SRC="$(find "$CORE_TMP/unpacked" -type f -name xray -print -quit)"
if [[ -z "$XRAY_SRC" ]]; then
    echo "ERROR: no xray binary found inside $CORE_ARCHIVE_NAME; wrong artifact?" >&2
    exit 1
fi
if ! "$XRAY_SRC" -version >/dev/null 2>&1; then
    echo "ERROR: downloaded xray from $CORE_ARCHIVE_NAME does not run on this machine." >&2
    exit 1
fi

cp "$XRAY_SRC" "$CORE_DIR/xray"
chmod +x "$CORE_DIR/xray"
echo "Installed core: $CORE_DIR/xray ($("$CORE_DIR/xray" -version | head -n1))"
rm -rf "$CORE_TMP"

# --- Auto-install the fresh bundle into /Applications ---
# Re-check the guard right before replacing the app (the early check above
# happened before the build; a lot of time has passed since).
abort_if_v2raymini_running
if [[ -d /Applications/v2rayMini.app ]]; then
    rm -rf /Applications/v2rayMini.app
fi
cp -R "$APP_DIR" /Applications/
echo "Installed app: /Applications/v2rayMini.app"
