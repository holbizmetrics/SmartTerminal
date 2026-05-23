# SmartTerminal — Roadmap

A .NET MAUI Android terminal that renders rich media inline (OSC 1338) and aims to
host developer CLIs (notably Claude Code). This roadmap records what's shipped, the
known limitations, and the architecture decisions for what's next — including a
cross-session architecture review (2026-05-23) that corrected the original plan.

## Status (2026-05-23)

**Shipped & verified on emulator + physical device:**
- **Inline rich media via OSC 1338** — mermaid / SVG / image / audio rendered inline
  in the terminal stream (parallel to OSC 1337 LaTeX/Markdown). Spec: `docs/OSC-1338-SPEC.md`.
  CLI emitter: `tools/tcat`. Proven rendering a mermaid diagram on two devices.
- **WebView layout fix** — `TerminalFrameLayout` re-measures children + `GetDesiredSize`
  returns the real constraint, so the xterm.js WebView actually gets dimensions
  (was collapsing to 0×0 → gray screen).
- **Pipe-based shell** — `PipeShellService` spawns `/system/bin/sh` via managed
  `System.Diagnostics.Process` (no NDK / no native code). Runs real commands
  (`echo`, `ls`, `pwd`, `cp`, `ping`, `printf` incl. OSC 1338) and streams output back.
  App-side line discipline (echo / backspace / Enter→run), gated by `IPtyService.EchoesInput`.
- Multi-tab sessions, predictive-keyboard + swipe capture (`SmartInputConnection`),
  UTF-8 output decoding, clipboard/paste, bundled assets (offline-ready).

**Known limitations (pipe shell vs a real PTY):**
- No interactive full-screen TUIs (vim, top, less) — they need a controlling TTY.
- No job control / Ctrl-C signal handling.
- No shell prompt (non-interactive `sh`).
- These are the boundary between "runs commands" and "full terminal" → see Tier 2.

## Load-bearing platform constraints (confirmed)

1. **Exec-from-data-dir is BLOCKED** on `targetSdk` ≥ 29 (Android 10+). SELinux
   (`execute_no_trans` on the `untrusted_app_29+` domain) refuses to `execve` a binary
   under `/data/data/<pkg>/files/`, even with the executable bit set.
   **→ Native executables MUST ship inside the APK's `lib/<abi>/` as `lib*.so`** (the
   package manager only extracts `lib*.so`, to the read-only `nativeLibraryDir`, which
   *is* exec-allowed). A static ELF executable named `libfoo.so` works. Requires
   `android:extractNativeLibs="true"`. This is already how the app ships `libpty`.
2. **`nativeLibraryDir` is read-only at runtime** — anything that needs to write
   (npm cache, configs, a rootfs) goes in `filesDir`/`cacheDir` via env vars
   (`HOME`, `XDG_CACHE_HOME`, `npm_config_cache`, …).
3. **Downloaded binaries still can't exec** — even spawned *from* `nativeLibraryDir`,
   a process can't exec other files it downloaded into the data dir. This bites tools
   whose `npm install` fetches/builds native postinstall binaries.

## Tier 2 — Real PTY (interactive programs)

Goal: run TUIs (vim, and ultimately Claude Code's full-screen UI) with raw mode,
`isatty`, and resize.

- **There is no managed/Java PTY path.** `android.system.Os` omits `openpty`/`forkpty`/
  `posix_openpt`, can't `grantpt`/`unlockpt`/`ptsname` (libc, not syscalls), and has no
  `ioctl` (so no `TIOCSWINSZ` resize). **A small amount of native code is unavoidable.**
- **Already built, correctly:** `Native/pty.c` (`forkpty` + `tcsetattr` + EINTR-safe
  read/write + `kill`/`waitpid` + `ioctl(TIOCSWINSZ)`, ~150 lines) → `libpty.so` per ABI
  via `build_native.sh`, wired in the `.csproj` as `<AndroidNativeLibrary>` with explicit
  `Abi=` and `Condition="Exists(...)"` (graceful degrade to `PipeShellService` when the
  `.so` is absent). Manifest sets `extractNativeLibs="true"`. This is the correct route
  for constraint #1 — confirmed by external review.
- **Build the lib:** the NDK is required only to *compile* `libpty.so`. `sdkmanager`
  needs JDK 17 (this machine has JDK 11), so prefer **downloading the NDK zip directly**
  from Google (no `sdkmanager`, no JDK) and compiling with its bundled `clang`.
- **Validation gate (do this BEFORE any Node work):** build `libpty.so`, switch
  `PtyServiceFactory.Create()` from `PipeShellService` to `PtyService`, and run **`vim`
  (or `htop`) against `/system/bin/sh`**. If `:q`, cursor movement, and resize-on-
  keyboard-show/hide all work → the PTY layer is shippable and Claude Code becomes "just
  packaging." If `vim` renders badly → fix that first; everything downstream (incl.
  Claude Code's full-screen UI) depends on it.
- **TUI throughput caveat:** output currently round-trips as base64 through
  `EvaluateJavascript("termWrite(...)")`. Fine for shell-paced output; may lag under a
  TUI repainting at ~60 Hz (alt-screen buffer, cursor save/restore). Upgrade path if it
  feels sluggish: `WebViewAssetLoader` + a custom scheme / `addJavascriptInterface` to
  post bytes straight into a JS callback (skip the JSON-string detour). Don't pre-optimize.

## Tier 3 — Claude Code

**You don't have to choose local vs. remote — `IPtyService` is the seam that supports
both.** Three implementations behind one interface, `PtyServiceFactory.Create()` picks
by config:
- `PtyService` — local PTY via `libpty.so` (`EchoesInput = true`)
- `PipeShellService` — managed no-NDK fallback (`EchoesInput = false`, UI echoes)
- `SshPtyService` *(future)* — remote transport to a user-owned host (`EchoesInput = true`,
  behaves like a real PTY). A clean drop-in; no architecture change.

**Recommendation (revised after external code review): finish LOCAL first.** The local
PTY layer is *already built and abstracted* — the marginal cost from here is small, so
harvest it. Add `SshPtyService` later as the remote option for users who own a host
(laptop on Tailscale, VPS); OSC 1338 inline rendering works identically over either.
(Earlier review said "thin-client first"; seeing the code flipped it — the local seam is
done, so finishing local is cheaper than starting a new transport.)

### Local Claude Code — the actual remaining gaps
1. **Static Node as `libnode.so`** in `Native/libs/<abi>/`, added to `.csproj` beside
   `libpty.so`. Reference: Termux's ARM64 Node build (reuse recipe or rename the binary).
   **ARM64 is the only field-relevant ABI** — but keep **x86_64 for emulator testing**;
   `armeabi-v7a` can be dropped.
2. **Frozen `node_modules` in assets.** `npm install @anthropic-ai/claude-code` on a dev
   machine, capture the tree, ship as a `MauiAsset`, first-run copy → `AppDataDirectory/`.
   **Runtime `npm install` stays disabled** (postinstall native binaries can't exec —
   constraint #3). Updates ship via app updates.
3. **PATH/HOME wiring** (`pty.c` + C#). Node can't be exec'd as `libnode.so` (argv[0]
   weirdness), so on first run create symlinks in `AppDataDirectory/bin/` → the real
   binaries in `nativeLibraryDir` (`Os.symlink` from managed code), and set the child
   env `PATH` to that `bin/` dir.
4. **Prove the TUI rendering path** (the `vim` gate in Tier 2) — Claude Code is a
   full-screen TUI; if `vim` doesn't render cleanly through xterm.js, Claude Code won't.
   Verify before doing the Node integration.

### NOT proot + rootfs
proot works outside Termux but adds 2-5× ptrace overhead, must itself live in
`nativeLibraryDir`, and can hit SELinux/ptrace denials on some OEM kernels. Static Node +
frozen `node_modules` avoids the entire userland. Only pay for proot if something
genuinely needs POSIX-everywhere.

## Decision log

- **2026-05-23 — proot demoted (settled).** Original plan led with proot + a Linux
  rootfs for Node. A fresh-context architecture review corrected this: static-Node-as-
  `.so` + frozen `node_modules` is the better local path; proot is off the menu. Content-
  level catch (verifiable Android exec/SELinux behavior + the npm-postinstall-exec
  constraint).
- **2026-05-23 — local-vs-thin: don't choose; `IPtyService` supports both.** The review
  first said "thin-client first," then **flipped after seeing the code**: the local PTY
  layer is already built + abstracted, so finishing local is the cheap path, and a future
  `SshPtyService` drops in behind the same interface for the remote case. So: finish
  local now, add SSH later — not either/or.
- **Sequencing (settled): prove the PTY/TUI path with `vim` BEFORE Node work.** Build
  `libpty.so` → switch the factory → run `vim`. Everything downstream depends on the TUI
  rendering cleanly.
- **Open / owed:** external validation (non-Claude / real device-matrix) before
  committing real effort to Tier 3; sourcing/building a static ARM64 Node is its own
  scoping rabbit hole (repackage Termux's vs. build `--fully-static`).
- **Parser note (do when next in `terminal.html`):** comment which OSC *dialect* we
  implement — our OSC 1337 (LaTeX/Markdown) + OSC 1338 (media) are *own allocations*,
  distinct from iTerm2's real OSC 1337 file protocol, so future-me doesn't conflate them.
