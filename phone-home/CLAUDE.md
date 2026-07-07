# SmartTerminal — environment briefing (installed 2026-07-07)

You are Claude Code 2.1.112 running inside **SmartTerminal**, a custom Android
terminal app (untrusted_app sandbox, Samsung S10 5G, Android 12). This is NOT
Termux. Read this instead of re-probing the environment.

## Installed
- **bash 5.2** (static musl) — your Bash tool shell, at `files/bin/bash` (on PATH)
- **node v26.3.1** (bionic) — full https/fetch networking works (DNS OK)
- **rg 15.0.0** — your Grep/Glob tools use it (on PATH)
- **toybox** at `/system/bin/toybox` — ls, cat, grep, sed, awk, find, tar, vi,
  nc, ping… (most also reachable directly via /system/bin on PATH)
- NOT installed: git, python, curl, wget, npm, ssh, gcc, any package manager.

## Known traps (verified on this device)
- **Pipelines can crash**: `X | head` may die with "Bad system call" (exit 159,
  seccomp). Avoid pipes where possible — use flags, globs, temp files, `>`
  redirection (which works fine), or do the processing in node.
- `/tmp` does not exist. Use `$TMPDIR` (set) or `$HOME`.
- Only `$HOME` (= files/home) and `$TMPDIR` are writable.
- Downloaded or newly created **binaries cannot execute** (SELinux W^X).
  Node **scripts** run fine (`node script.js`); shell scripts via `bash script.sh`
  (never `./script.sh`).
- The user's keyboard (SwiftKey) may autocapitalize the first typed word.

## Capabilities to actively use
- **Network via node**: https/fetch works. For GitHub read access no auth is
  needed (REST API): see `~/gh-fetch.js` / `~/gh-read.js` if present — you
  wrote them. Pattern: api.github.com with a User-Agent header.
- **INLINE MEDIA — this terminal renders images, SVG, Mermaid and audio
  inline!** Use `node ~/tcat.js <file>` (.svg .png .jpg .mmd .mp3 …) or
  `node ~/tcat.js --type mermaid < diagram.mmd`. When a diagram, chart (write
  SVG), or image would communicate better than text — SHOW it.
- The user can run a shell command inside your session with the `!` prefix.

## House rules
- git arrives via a future app update (runtime package manager planned);
  until then use the GitHub REST pattern for read access.
- NEVER update Claude Code itself: 2.1.112 is pinned deliberately — newer
  versions ship a native binary whose DNS cannot work on Android.
