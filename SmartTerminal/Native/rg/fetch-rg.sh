#!/usr/bin/env bash
# Fetch STATIC aarch64-musl ripgrep (microsoft/ripgrep-prebuilt — the VS Code builds;
# upstream BurntSushi ships no aarch64-musl asset) as librg.so. NodeRuntimeService
# symlinks files/bin/rg -> nativeLibraryDir/librg.so; the claude alias already sets
# USE_BUILTIN_RIPGREP=0 (vendored rg can't exec from filesDir, SELinux W^X), so
# Claude Code picks this rg up from PATH. Payload gitignored; re-run after clone.
# Behind a corporate TLS wall use PowerShell Invoke-WebRequest (SChannel), not curl.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p arm64-v8a
VER="v15.0.1"
URL="https://github.com/microsoft/ripgrep-prebuilt/releases/download/$VER/ripgrep-$VER-aarch64-unknown-linux-musl.tar.gz"
curl -L ${CURL_FLAGS:-} -o rg.tar.gz "$URL"
tar xzf rg.tar.gz rg
mv rg arm64-v8a/librg.so
rm rg.tar.gz
echo "fetched arm64-v8a/librg.so ($(wc -c < arm64-v8a/librg.so) bytes)"
