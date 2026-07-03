#!/usr/bin/env bash
# Fetch the Claude Code native binary (musl build) + Alpine musl loader and lay them
# out as APK-legal lib*.so payloads under Native/claude/<abi>/.
#
# Why this shape: Claude Code ships as platform-specific compiled binaries; the
# linux-*-musl variant depends ONLY on musl libc, and musl's loader can be exec'd
# directly with the target binary as argv[1]. Both live in nativeLibraryDir where
# exec + PROT_EXEC are allowed. Probe-verified on Android emulator 2026-07-03
# (see ROADMAP decision log). Payloads are ~240 MB/ABI — never committed.
#
# Usage: ./fetch-claude-libs.sh [arm64-v8a|x86_64]...   # default: both
# Corporate TLS interception: export CURL_FLAGS=--ssl-no-revoke

set -euo pipefail
cd "$(dirname "$0")"

CLAUDE_VERSION="${CLAUDE_VERSION:-2.1.200}"
ALPINE_BRANCH="${ALPINE_BRANCH:-v3.22}"
CURL_FLAGS="${CURL_FLAGS:-}"
ABIS=("$@"); [ $# -eq 0 ] && ABIS=(arm64-v8a x86_64)

for ABI in "${ABIS[@]}"; do
  case "$ABI" in
    arm64-v8a) NPM_ARCH=arm64; ALPINE_ARCH=aarch64;;
    x86_64)    NPM_ARCH=x64;   ALPINE_ARCH=x86_64;;
    *) echo "unsupported ABI $ABI" >&2; exit 1;;
  esac
  WORK=$(mktemp -d); mkdir -p "$ABI"
  echo "== $ABI (claude $CLAUDE_VERSION, musl $ALPINE_BRANCH/$ALPINE_ARCH)"

  # Claude Code musl binary from the npm registry tarball
  PKG="claude-code-linux-$NPM_ARCH-musl"
  curl -fsSL $CURL_FLAGS "https://registry.npmjs.org/@anthropic-ai/$PKG/-/$PKG-$CLAUDE_VERSION.tgz" -o "$WORK/pkg.tgz"
  tar -xzf "$WORK/pkg.tgz" -C "$WORK" package/claude
  cp "$WORK/package/claude" "$ABI/libclaude.so"

  # musl loader from the current Alpine musl package
  IDX=$(curl -fsSL $CURL_FLAGS "https://dl-cdn.alpinelinux.org/alpine/$ALPINE_BRANCH/main/$ALPINE_ARCH/")
  MUSL_APK=$(echo "$IDX" | grep -o "musl-1[^\"]*\.apk" | head -1)
  curl -fsSL $CURL_FLAGS "https://dl-cdn.alpinelinux.org/alpine/$ALPINE_BRANCH/main/$ALPINE_ARCH/$MUSL_APK" -o "$WORK/musl.apk"
  tar -xzf "$WORK/musl.apk" -C "$WORK" "lib/ld-musl-$ALPINE_ARCH.so.1" 2>/dev/null
  cp "$WORK/lib/ld-musl-$ALPINE_ARCH.so.1" "$ABI/libmuslld.so"

  rm -rf "$WORK"
  echo "   $(du -sh "$ABI" | cut -f1)"
done
echo "Done. Rebuild the app; the .csproj picks the payloads up automatically."
