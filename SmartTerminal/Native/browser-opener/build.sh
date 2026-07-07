#!/usr/bin/env bash
# Build libbrowseropener.so (arm64) — the $BROWSER opener that signals the app
# (writes $TMPDIR/open-url) so C# can Browser.OpenAsync the OAuth login URL.
# Two toolchain routes, either produces a working binary:
#   1. NDK clang (bionic, dynamic)  — needs ANDROID_NDK_HOME or the SDK ndk/ dir.
#   2. zig cc (musl, STATIC)        — needs only zig (ZIG env var or on PATH).
#      Static musl runs fine on Android (no loader/libc deps).
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p arm64-v8a

NDK="${ANDROID_NDK_HOME:-}"
[ -z "$NDK" ] && NDK=$(ls -d "${ANDROID_SDK_ROOT:-/c/Program Files (x86)/Android/android-sdk}/ndk/"* 2>/dev/null | sort -V | tail -1 || true)
CLANG=$(ls "$NDK"/toolchains/llvm/prebuilt/*/bin/clang* 2>/dev/null | grep -E 'clang(\.exe)?$' | head -1 || true)

if [ -n "$CLANG" ]; then
  "$CLANG" --target=aarch64-linux-android24 -O2 -Wl,-z,max-page-size=16384 \
    -o arm64-v8a/libbrowseropener.so browser_open.c
  echo "built arm64-v8a/libbrowseropener.so (NDK clang)"
else
  ZIG="${ZIG:-zig}"
  command -v "$ZIG" >/dev/null || { echo "neither NDK clang nor zig found; set ANDROID_NDK_HOME or ZIG" >&2; exit 1; }
  "$ZIG" cc -target aarch64-linux-musl -static -Oz -Wl,-s -Wl,-z,max-page-size=16384 \
    -o arm64-v8a/libbrowseropener.so browser_open.c
  echo "built arm64-v8a/libbrowseropener.so (zig static musl)"
fi
