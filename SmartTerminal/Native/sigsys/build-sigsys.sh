#!/usr/bin/env bash
# Compile the SIGSYS->ENOSYS shim for arm64-v8a and place it beside the claude
# payload so the .csproj bundles it. Needs an NDK; point ANDROID_NDK_HOME at it,
# or the script tries the SDK's ndk/ dir.
#
# Freestanding: -nostdlib, no libc dependency (must be DT_NEEDED-free so a musl
# process can LD_PRELOAD it without pulling bionic). ARM64 only — the phone is the
# only place the strict seccomp filter bites; the API-36 emulator passes without it.
set -euo pipefail
cd "$(dirname "$0")"

NDK="${ANDROID_NDK_HOME:-}"
if [ -z "$NDK" ]; then
  SDK="${ANDROID_SDK_ROOT:-/c/Program Files (x86)/Android/android-sdk}"
  NDK=$(ls -d "$SDK/ndk/"* 2>/dev/null | sort -V | tail -1 || true)
fi
[ -n "$NDK" ] && [ -d "$NDK" ] || { echo "No NDK found. Set ANDROID_NDK_HOME." >&2; exit 1; }

CLANG=$(ls "$NDK"/toolchains/llvm/prebuilt/*/bin/clang* 2>/dev/null | grep -E 'clang(\.exe)?$' | head -1)
[ -n "$CLANG" ] || { echo "clang not found under $NDK" >&2; exit 1; }
echo "NDK:   $NDK"
echo "clang: $CLANG"

OUT_DIR=../claude/arm64-v8a
mkdir -p "$OUT_DIR"
"$CLANG" --target=aarch64-linux-android24 \
  -O2 -fPIC -shared -nostdlib -Wl,--no-undefined \
  -Wl,-z,max-page-size=16384 \
  -o "$OUT_DIR/libsigsys2enosys.so" libsigsys2enosys.c

echo "Built $OUT_DIR/libsigsys2enosys.so"
# Sanity: must have zero NEEDED libs (no bionic dependency).
READELF=$(ls "$NDK"/toolchains/llvm/prebuilt/*/bin/llvm-readelf* 2>/dev/null | head -1)
if [ -n "$READELF" ]; then
  echo "--- dynamic (expect NO 'NEEDED') ---"
  "$READELF" -d "$OUT_DIR/libsigsys2enosys.so" | grep -E 'NEEDED|INIT' || echo "(clean: no NEEDED)"
fi
