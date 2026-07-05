#!/usr/bin/env bash
# Build claude-js.zip: the Claude Code 2.1.112 pure-JS runtime, stripped to arm64,
# for bundling as a MauiAsset. 2.1.112 is the LAST pure-JS release (bin: cli.js);
# 2.1.113+ became a thin wrapper around a compiled musl binary (the one that hits the
# Android DNS wall). Running cli.js on the bundled bionic Node resolves DNS via netd.
#
# Not committed (~6 MB zip); re-run after a fresh clone. Needs npm + python3.
# Usage: ./fetch-claude-js.sh [version]   (default 2.1.112)
set -euo pipefail
cd "$(dirname "$0")"

VER="${1:-2.1.112}"
WORK=$(mktemp -d)
echo "== claude-code@$VER -> claude-js.zip (arm64-linux only)"

( cd "$WORK" && npm pack "@anthropic-ai/claude-code@$VER" --silent >/dev/null && tar -xzf *.tgz )
SRC="$WORK/package"
STAGE="$WORK/stage"
mkdir -p "$STAGE/vendor/ripgrep/arm64-linux" "$STAGE/vendor/audio-capture/arm64-linux"
cp "$SRC/cli.js" "$SRC/package.json" "$SRC/LICENSE.md" "$SRC/sdk-tools.d.ts" "$STAGE/" 2>/dev/null || true
cp "$SRC/vendor/ripgrep/arm64-linux/rg" "$STAGE/vendor/ripgrep/arm64-linux/" 2>/dev/null || true
cp "$SRC/vendor/audio-capture/arm64-linux/audio-capture.node" "$STAGE/vendor/audio-capture/arm64-linux/" 2>/dev/null || true

# Zip via python for a stable, path-safe archive (bash zip mangles on some Windows setups).
python3 - "$STAGE" "$(pwd)/claude-js" <<'EOF'
import shutil, sys
shutil.make_archive(sys.argv[2], "zip", sys.argv[1])
EOF
rm -rf "$WORK"
echo "Built $(pwd)/claude-js.zip"
