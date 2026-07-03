#!/usr/bin/env bash
# Fetch the bundled Node.js runtime from the Termux package repo and lay it out
# as APK-legal lib*.so payloads under Native/node/<abi>/.
#
# Why: node + deps are ~93 MB per ABI — too heavy to commit. The .csproj bundles
# them only when present (Exists() condition, same pattern as libpty.so).
# Route + soname findings probe-verified 2026-07-03 (see ROADMAP decision log).
#
# Usage: ./fetch-node-libs.sh            # both ABIs
#        ./fetch-node-libs.sh x86_64     # one ABI
# On corporate networks with TLS interception you may need: export CURL_FLAGS=--ssl-no-revoke

set -euo pipefail
cd "$(dirname "$0")"

REPO="https://packages.termux.dev/apt/termux-main"
CURL_FLAGS="${CURL_FLAGS:-}"
ABIS=("${@:-arm64-v8a x86_64}")
[ $# -eq 0 ] && ABIS=(arm64-v8a x86_64)

# APK abi dir -> Termux arch name
arch_for() { case "$1" in arm64-v8a) echo aarch64;; x86_64) echo x86_64;; *) echo "unsupported ABI $1" >&2; exit 1;; esac; }

PKGS=(nodejs libc++ openssl c-ares libicu libsqlite zlib libffi)
# canonical lib names we keep (everything else in the debs is dropped)
LIBS=(libc++_shared.so libcares.so libcrypto.so libffi.so libicudata.so libicui18n.so libicuuc.so libsqlite3.so libssl.so libz.so)

for ABI in "${ABIS[@]}"; do
  ARCH=$(arch_for "$ABI")
  WORK=$(mktemp -d)
  echo "== $ABI ($ARCH) -> $WORK"
  curl -fsSL $CURL_FLAGS "$REPO/dists/stable/main/binary-$ARCH/Packages" -o "$WORK/Packages"

  python3 - "$WORK/Packages" "${PKGS[@]}" <<'EOF' > "$WORK/urls"
import re, sys
want = set(sys.argv[2:])
for block in open(sys.argv[1], encoding="utf-8", errors="replace").read().split("\n\n"):
    m = re.search(r"^Package: (.+)$", block, re.M)
    if m and m.group(1) in want:
        print(re.search(r"^Filename: (.+)$", block, re.M).group(1).replace(":", "%3a"))
EOF

  mkdir -p "$WORK/rootfs" "$ABI"
  while read -r f; do
    curl -fsSL $CURL_FLAGS "$REPO/$f" -o "$WORK/$(basename "${f//%3a/_}")"
  done < "$WORK/urls"

  python3 - "$WORK" <<'EOF'
import glob, io, os, sys, tarfile
work = sys.argv[1]
for deb in glob.glob(os.path.join(work, "*.deb")):
    data = open(deb, "rb").read()
    assert data[:8] == b"!<arch>\n", deb
    off = 8
    while off < len(data):
        name = data[off:off+16].decode().strip()
        size = int(data[off+48:off+58].decode().strip())
        if name.startswith("data.tar"):
            with tarfile.open(fileobj=io.BytesIO(data[off+60:off+60+size]), mode="r:*") as tf:
                tf.extractall(os.path.join(work, "rootfs"), filter="tar")
            break
        off += 60 + size + (size % 2)
EOF

  USR="$WORK/rootfs/data/data/com.termux/files/usr"
  cp "$USR/bin/node" "$ABI/libnode.so"
  for lib in "${LIBS[@]}"; do cp "$USR/lib/$lib" "$ABI/"; done
  rm -rf "$WORK"
  echo "   $(ls "$ABI" | wc -l) files, $(du -sh "$ABI" | cut -f1)"
done
echo "Done. Rebuild the app; the .csproj picks the payloads up automatically."
