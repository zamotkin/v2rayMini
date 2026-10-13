#!/usr/bin/env bash
# Local deploy script: build the macOS arm64 self-contained app and zip it.
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

# ditto preserves unix exec permissions (plain zip can strip them and break
# the app); on non-mac fall back to zip -r -y.
if [[ "$(uname -s)" == "Darwin" ]]; then
    ditto -c -k --keepParent "$OUT_DIR" "$ZIP_PATH"
else
    (cd dist && zip -r -y v2rayMini-osx-arm64.zip v2rayMini-osx-arm64)
fi

ABS_ZIP_PATH="$(cd "$(dirname "$ZIP_PATH")" && pwd)/$(basename "$ZIP_PATH")"
ABS_ZIP_SIZE="$(du -h "$ABS_ZIP_PATH" | cut -f1)"
echo "Artifact: $ABS_ZIP_PATH ($ABS_ZIP_SIZE)"
