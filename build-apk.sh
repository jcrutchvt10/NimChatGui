#!/usr/bin/env bash
set -euo pipefail

# ===================================================
# Build script for NimChatGui.Android APK
# ===================================================
# Prerequisites:
#   - .NET 8 SDK with MAUI Android workload
#   - Android SDK (API 34+)
#   - Java JDK 17+
#
# Supported platforms: Windows, macOS, Linux (x86_64)
# Linux ARM64 users should use Docker or GitHub Actions.
# ===================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
CONFIGURATION="${1:-Release}"

echo "==> Building NimChatGui.Android ($CONFIGURATION)"
echo "    Project: $PROJECT_DIR"

# ---- Step 1: Check prerequisites ----
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET SDK not found. Install from https://dotnet.microsoft.com/download"
    exit 1
fi

echo "==> .NET version: $(dotnet --version)"

# ---- Step 2: Restore workloads ----
echo "==> Restoring .NET workloads..."
sudo dotnet workload install maui-android 2>/dev/null || dotnet workload install maui-android

# ---- Step 3: Restore NuGet packages ----
echo "==> Restoring NuGet packages..."
dotnet restore "$PROJECT_DIR/NimChatGui.Android.csproj"

# ---- Step 4: Build & publish APK ----
echo "==> Building APK..."
dotnet publish "$PROJECT_DIR/NimChatGui.Android.csproj" \
    -f net8.0-android \
    -c "$CONFIGURATION" \
    -p:AndroidSigningKeyStore= \
    -p:AndroidSigningKeyAlias= \
    -p:AndroidSigningKeyPass= \
    -p:AndroidSigningStorePass=

APK_DIR="$PROJECT_DIR/bin/$CONFIGURATION/net8.0-android/publish"
echo ""
echo "========================================"
echo "  Build complete!"
echo "  APK location: $APK_DIR"
echo "========================================"
ls -lh "$APK_DIR"/*.apk 2>/dev/null || echo "  (check output directory for APK)"
