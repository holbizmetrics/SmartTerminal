#!/usr/bin/env bash
# Build libbrowseropener.so (arm64) — the $BROWSER opener that lets claude auto-open
# the OAuth login via `am start`. Needs an NDK (ANDROID_NDK_HOME or the SDK ndk/ dir).
set -euo pipefail
cd "$(dirname "$0")"
NDK="${ANDROID_NDK_HOME:-}"
[ -z "$NDK" ] && NDK=$(ls -d "${ANDROID_SDK_ROOT:-/c/Program Files (x86)/Android/android-sdk}/ndk/"* 2>/dev/null | sort -V | tail -1 || true)
CLANG=$(ls "$NDK"/toolchains/llvm/prebuilt/*/bin/clang* 2>/dev/null | grep -E 'clang(\.exe)?$' | head -1)
[ -n "$CLANG" ] || { echo "clang not found; set ANDROID_NDK_HOME" >&2; exit 1; }
mkdir -p arm64-v8a
"$CLANG" --target=aarch64-linux-android24 -O2 -Wl,-z,max-page-size=16384 \
  -o arm64-v8a/libbrowseropener.so browser_open.c
echo "built arm64-v8a/libbrowseropener.so"
