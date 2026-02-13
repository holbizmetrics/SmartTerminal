#!/bin/bash
# build_native.sh — Build libpty.so for all Android ABIs using NDK.
#
# Prerequisites:
#   - Android NDK installed (set ANDROID_NDK_HOME or NDK_HOME)
#   - API level 26+ (Android 8.0+)
#
# Usage:
#   chmod +x build_native.sh
#   ./build_native.sh

set -e

NDK="${ANDROID_NDK_HOME:-${NDK_HOME:-$HOME/Android/Sdk/ndk/27.0.12077973}}"
API=26
SRC="pty.c"

if [ ! -d "$NDK" ]; then
    echo "ERROR: Android NDK not found at $NDK"
    echo "Set ANDROID_NDK_HOME or NDK_HOME environment variable."
    exit 1
fi

TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/linux-x86_64/bin"

if [ ! -d "$TOOLCHAIN" ]; then
    # macOS
    TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/darwin-x86_64/bin"
fi

if [ ! -d "$TOOLCHAIN" ]; then
    echo "ERROR: NDK toolchain not found."
    exit 1
fi

# Build for each ABI
declare -A TARGETS=(
    ["arm64-v8a"]="aarch64-linux-android${API}-clang"
    ["armeabi-v7a"]="armv7a-linux-androideabi${API}-clang"
    ["x86_64"]="x86_64-linux-android${API}-clang"
)

for ABI in "${!TARGETS[@]}"; do
    CC="${TOOLCHAIN}/${TARGETS[$ABI]}"
    OUTDIR="libs/${ABI}"
    mkdir -p "$OUTDIR"

    echo "Building libpty.so for ${ABI}..."
    "$CC" -shared -fPIC -O2 -Wall \
        -o "${OUTDIR}/libpty.so" \
        "$SRC" \
        -lutil

    echo "  → ${OUTDIR}/libpty.so"
done

echo ""
echo "Done. Copy libs/ into SmartTerminal/Native/libs/"
