#!/usr/bin/env bash
# Fetch a STATIC aarch64 bash (robxu9/bash-static, musl-based, ~2.3 MB) as libbash.so.
# Claude Code's shell detection (cli.js NzY) only accepts a shell whose PATH STRING
# contains "bash" or "zsh" — mksh can never qualify — so we ship real bash and
# NodeRuntimeService symlinks files/bin/bash -> nativeLibraryDir/libbash.so + sets SHELL.
# Payload gitignored; re-run after fresh clone. Behind a corporate TLS wall use
# PowerShell Invoke-WebRequest (SChannel trusts the MITM cert; curl does not).
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p arm64-v8a
URL="https://github.com/robxu9/bash-static/releases/download/5.2.015-1.2.3-2/bash-linux-aarch64"
curl -L ${CURL_FLAGS:-} -o arm64-v8a/libbash.so "$URL"
echo "fetched arm64-v8a/libbash.so ($(wc -c < arm64-v8a/libbash.so) bytes)"
