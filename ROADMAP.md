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

- **2026-07-08 (late) — stpkg arc STAGE 1 committed, UNVERIFIED on device (`5bfdf80`).**
  Manifest `targetSdkVersion=28` (the Termux trick — SELinux permits app-storage exec only
  for targetSdk<=28); merged manifest confirms 28; .NET toolchain builds clean (XA1006/XA4211
  warnings expected, downgrade 36->28 legal since both above the runtime-perms boundary).
  Added `RuntimeExecSelfTest` to boot diagnostics: copies libbash->tmp, chmod 0700, runs it,
  logs `runtime-exec self-test OK` (app storage executable -> stpkg installs possible) or
  `BLOCKED` (W^X still enforced). **This log line is the arc's foundation probe — read it first
  next session.** Deploy was blocked twice: phone disk 100% full (YouTube Smart-Downloads had
  silently auto-fetched a whole playlist as "recommended downloads" — ~27 GB; cleared
  `/sdcard/Android/data/com.google.android.youtube/files/offline` -> 27 GB free), then a UI
  hang + forced reboot during the mass-delete I/O storm. Phone recovered clean. **NEXT: replug,
  deploy, read the verdict. If OK -> write stpkg (node downloader + install into files/opt +
  symlink into files/bin); first target git.** Storage note: turn OFF YouTube Smart Downloads
  or it refills the phone; the S10 lives chronically near-full (228 GB, was at 185 MB free).

- **2026-07-07 (night) — bash + rg SHIPPED; first real claude tool-calls ran on-device.
  Package-manager route DECIDED-PENDING: targetSdk 28 + runtime installs (own arc).**
  Post-login claude threw "Claude CLI requires a Posix shell environment": its shell
  detection (cli.js NzY) only accepts a shell whose PATH STRING contains "bash"/"zsh" —
  mksh can never qualify. Shipped robxu9/bash-static 5.2.15 (static aarch64-musl, 2.3 MB)
  as libbash.so + bin/bash symlink + SHELL env; terminal stays mksh (pty.c takes its shell
  explicitly). Proof it matters: claude's Bash tool then executed real commands through it
  (screenshot: `git pull` → "git: command not found" — correct, no git in sandbox).
  Also shipped ripgrep 15.0.0 (microsoft/ripgrep-prebuilt aarch64-MUSL — upstream has no
  such asset) as librg.so + bin/rg; with USE_BUILTIN_RIPGREP=0 already set, claude's search
  tools now resolve rg from PATH. Boot self-tests generalized (ToolSelfTest) — static musl
  binaries can't take the sigsys LD_PRELOAD shim, so the boot log line is the per-device
  seccomp proof; both green on SM-G977B/Android 12.
  **git deliberately NOT baked:** no prebuilt static aarch64 git with git-remote-https
  exists (the https transport is the part static builds skip), and repacking Termux git
  means a multi-lib + helper-exe dance. The better route is the PACKAGE MANAGER arc:
  drop targetSdkVersion to 28 (the Termux trick — SELinux allows app-data exec only for
  targetSdk ≤ 28; side-loaded app, Play rules irrelevant) → runtime-downloaded binaries
  become executable → a small stpkg (node-based downloads) can install git/jq/python from
  Termux debs or static builds WITHOUT rebuilds. Next session's opener.

- **2026-07-07 (evening, later) — LOGIN COMPLETE. Claude Code authenticated + live in-app.
  The app-signal fix VERIFIED end-to-end on device — and the login closed ITSELF.**
  Deploy: `dotnet build -t:Install` onto SM-G977B (2.6 GB free sufficed, no trim needed).
  Logcat: `open-url watcher armed` + node/claude self-tests OK. Mechanical e2e test first
  (run-as exec of the opener with https://example.com, TMPDIR set): opener exit 0 →
  `open-url -> browser` → Brave CustomTab foreground — browser overlay opens ON TOP of the
  terminal (Browser.OpenAsync default), ideal login UX. Then the real run: claude's OAuth URL
  auto-opened at 19:02, and because claude 2.1.112 runs a loopback callback server
  (`redirect_uri=localhost:33037/callback`) and the browser is on the same device, the OAuth
  loop completed WITHOUT the code-paste step — `.credentials.json` + first `history.jsonl`
  entry on disk at 19:02. The "Paste code here >" wall from both prior sessions is gone.
  Ops notes: adb didn't enumerate the plugged phone until a second kill-server cycle
  (Windows saw the SAMSUNG ADB interface all along — restart adb before suspecting cable);
  Android's hashed codePath contains `==`, which breaks naive `sed 's/.*=//'` extraction.
  REMAINING (all quality-of-life, no blockers): rg-as-native-lib, 16 KB libpty rebuild,
  parked arrow-keys, task-2 autocap fork (operator call), console-crash watch (did not recur).
  `browser_open.c` v2: writes the URL to `$TMPDIR/open-url` (atomic tmp+rename), keeps the old
  `am start` exec only as a shell-domain fallback. `NodeRuntimeService` arms an
  `Android.OS.FileObserver` on `files/tmp` at Setup(); on `open-url` it validates the scheme
  (http/https only), deletes the file, and calls `Browser.OpenAsync` from the app process —
  which IS allowed to start activities. Compile green (0 errors).
  **Toolchain note:** the NDK from 07-05 survived only partially (sysroot, no clang) and this
  box's corporate wall blocks curl (SChannel/PowerShell passes) — so the opener is now built
  with **zig cc as a static aarch64-musl binary** (25 KB, no loader/libc deps; build.sh tries
  NDK clang first, falls back to zig). Static musl on Android: same trick as the ld-musl claude
  probe, minus the loader.
  **Also confirmed in code:** `SetupClaude` rewrites `.mkshrc` whenever content differs — the
  07-07 on-phone stopgap alias was therefore erased at next app start, as suspected.
  **Task 2 (no-autocap inputType) PARKED — design fork:** `TextFlagNoSuggestions` /
  visible-password would kill the SwiftKey prediction bar = the founding feature; there is no
  InputType flag meaning "predictions yes, autocap no" (current code sets no cap flags and
  SwiftKey autocaps from its own heuristics). Options: settings toggle, NoSuggestions default,
  or live-with-it. Operator call owed.

- **2026-07-07 — LOGIN ARC: browser auto-open ROOT-CAUSED DEAD on device; zero-rebuild stopgap wired; login not yet completed (session closed at the URL stage).**
  The f0e77f0 opener FAILS on the phone — not at exec (the .so runs fine from nativeLibraryDir),
  but at `am` itself: `cmd: Failure calling service activity: Failed transaction (2147483646)`,
  exit 2. `/system/bin/am`'s binder call to the activity service is DENIED for the untrusted_app
  domain on Android 12 — this is exactly why Termux ships its own reimplemented `am` (termux-am).
  Diagnostic method that got the error visible: append the opener invocation to `.mkshrc` via
  `adb run-as` + open a new terminal tab (auto-runs at shell start) — adb `input text` is useless
  for paths here because SwiftKey's predictive engine mangles injected text whenever the compose
  buffer is dirty (clean prompt → clean injection, dirty → garbage).
  **Permanent fix (next build): the opener must signal the APP, not call `am`** — write the URL to
  `files/tmp/open-url` and have C# (FileSystemWatcher or poll in NodeRuntimeService) call
  `Browser.OpenAsync(url)`; the app process is allowed to start activities. (Alternative: vendor a
  termux-am-style app_process client — heavier.)
  **Stopgap wired ON DEVICE (ephemeral, not in repo):** `.mkshrc` alias now sets
  `BROWSER=/system/bin/log` → claude hands the URL to toybox `log` → it lands in logcat un-wrapped
  → desktop fires `adb shell am start -a android.intent.action.VIEW -d "<url>"` (SHELL domain may
  call am). CAVEATS: unverified whether `SetupClaude` rewrites `.mkshrc` at app start (would erase
  the stopgap); the one live run still printed the "Browser didn't open?" fallback and NO log line
  was captured — possibly run from an old-alias tab; re-verify next session.
  **State at close:** claude 2.1.112 walks onboarding cleanly (theme → subscription login) and sits
  at "Paste code here if prompted >" with a valid OAuth URL printed. Manual completion path that
  needs no fix at all: open the printed URL in ANY browser (desktop fine — account-bound, not
  device-bound), log in, copy the code, long-press-paste it into the phone terminal.
  Also: 07-05's console-crash did NOT recur (app alive the whole session). Install on the chronically
  full phone: `adb shell pm trim-caches 6G` freed 1.3 GB → 54 MB fast-deploy APK installed fine.
  NEW TASKS: (1) opener→app-signal fix above; (2) terminal input view should set
  no-suggestions/no-autocap inputType — SwiftKey autocapitalized the operator's `claude` into
  `Claude` ("inaccessible or not found"), a first-run trap for every command.

- **2026-07-05 — DNS wall hit + BETTER PATH found (pivot to 2.1.112 pure-JS). VERIFIED vs npm.**
  The musl claude binary launches + renders full-screen TUI on the phone (Claude mascot, colors,
  onboarding — all clean), but the API call fails: `Failed to connect to api.anthropic.com`.
  Root cause: claude is **musl**, musl does DNS via `/etc/resolv.conf` which **does not exist on
  Android** (confirmed absent; Android resolves via netd, which only bionic knows). ping works
  (system/bionic); claude can't resolve. **The fix is a version pivot, not a DNS hack:**
  `@anthropic-ai/claude-code` was pure JavaScript through **2.1.112** (`bin: cli.js`, 49 MB,
  self-contained, zero deps) and flipped to a thin wrapper→compiled-native-binary at **2.1.113**
  (`bin: bin/claude.exe` + per-platform `...-musl` optionalDeps). So run **2.1.112 cli.js on our
  bundled Termux Node, which is BIONIC** → DNS resolves via netd (node/fetch already proven to
  work), AND bionic Node is seccomp-allowlisted → **the SIGSYS shim isn't even needed for this
  path**, AND no musl loader / no 241 MB libclaude.so. Native surface of 2.1.112 is tiny: `cli.js`
  (JS) + optional `vendor/ripgrep/arm64-linux/rg` (escape hatch: `USE_BUILTIN_RIPGREP=0` + system
  rg, as Termux does) + optional audio-capture.node + optional sharp. Operator runs 2.1.112 on
  Termux daily — proven to work on arm64/bionic.
  **IMPLEMENTED + startup-verified 2026-07-05:** `claude-js/` bundle (2.1.112 cli.js stripped to
  arm64, zipped, MauiAsset `claude-js.zip`, fetched by `fetch-claude-js.sh`, gitignored ~6 MB).
  `NodeRuntimeService.SetupClaude` now extracts the zip to filesDir on first run and aliases
  `claude` -> `node cli.js` (+ USE_BUILTIN_RIPGREP=0). Musl path gated OFF by default
  (`-p:BundleMuslClaude=true` to re-enable). Phone logcat: `claude-js extracted` +
  `claude self-test OK: 2.1.112`. App ~230 MB smaller (no libclaude.so). NETWORK test pending
  (the point of the pivot); rg-as-native-lib still owed for search tools.

- **2026-07-05 — CLAUDE CODE RUNS ON THE PHONE IN-APP. Tier 3 seccomp blocker CLOSED.**
  `Native/sigsys/libsigsys2enosys.c` — freestanding aarch64 LD_PRELOAD shim (3 KB, zero
  DT_NEEDED so it can't collide with the process's musl; built via NDK r27c, `build-sigsys.sh`).
  Installs a SIGSYS handler that sets x0=-ENOSYS and returns WITHOUT advancing PC (aarch64 ELR
  already points past the svc). Wired into the claude alias + self-test as LD_PRELOAD (scoped to
  claude, not node). On SM-G977B/Android 12, in the terminal UI:
  `sigsys2enosys: converted syscall 436` then `2.1.200 (Claude Code)`. **Syscall 436 = close_range**
  (NOT clone3 as predicted — vindicates the syscall-agnostic design; close_range is a pure fd-cleanup
  optimization musl falls back from cleanly, so ENOSYS-substitution is safe). Exactly one syscall
  converted. logcat self-tests: node v26.3.1 + claude 2.1.200 both OK in untrusted_app.
  Deploy note: manual `adb install` of a Debug APK crashes ("No assemblies … Fast Deployment") —
  the managed assemblies deploy separately; use `dotnet build -t:Install` (fast-deploy) OR
  `-p:EmbedAssembliesIntoApk=true`. Phone install needs ~2 GB free (claude payload is 241 MB;
  arm64-only build via `-p:AndroidSupportedAbis=arm64-v8a`).

- **2026-07-03 — PHONE (SM-G977B, Android 12): terminal + SwiftKey + PTY + Node ALL VERIFIED;
  claude blocked by app-domain seccomp — fix identified.** Operator screenshot: SwiftKey
  predictive input committing words into the real PTY shell (the founding feature, live).
  Logcat: `node self-test OK: v26.3.1` (ARM64). `claude self-test FAILED: exit=159` = SIGSYS:
  Android 12's untrusted_app seccomp allowlist KILLS syscalls it doesn't know, where a plain
  kernel would return ENOSYS — musl/bun probe newer syscalls expecting ENOSYS-fallback.
  Proof of mechanism: the identical binary pair in /data/local/tmp (shell domain, no app
  filter) prints `2.1.200 (Claude Code)` exit 0 ON THE SAME PHONE. (API-36 emulator's newer
  allowlist passes in-app.) NEXT: freestanding aarch64 shim `libsigsys2enosys.so` — SIGSYS
  handler rewrites the faulting syscall's return to -ENOSYS and resumes; inject via musl
  loader LD_PRELOAD in the claude alias. Needs NDK (~60-line -nostdlib .so; remember
  -Wl,-z,max-page-size=16384). Also shipped: shells now start in $HOME via .mkshrc (app cwd
  "/" is unlistable for untrusted_app). Claude Code now ships as native binaries; musl-loader
  route PROBE-VERIFIED on emulator.** Since ~v2.x, `@anthropic-ai/claude-code` is a thin wrapper
  around platform-specific compiled single binaries (optionalDependencies) — the May-era
  "frozen node_modules + Node runs cli.js" plan is obsolete. The good variant exists:
  `@anthropic-ai/claude-code-linux-{arm64,x64}-musl`. The binary is dynamic against musl only
  (`PT_INTERP /lib/ld-musl-*.so.1`, DT_NEEDED = musl libc alone), and musl's loader can be
  exec'd directly with the target as argv[1]. Probe: Alpine `ld-musl-x86_64.so.1` (662 KB) +
  `claude` (246 MB) in /data/local/tmp → `./ld-musl-x86_64.so.1 ./claude --version` →
  `2.1.200 (Claude Code)`, exit 0. Plan now: ship both as `libmuslld.so` + `libclaude.so`
  in nativeLibraryDir (exec + PROT_EXEC allowed there), alias `claude` in the shell rc.
  Node runtime stays as a general terminal capability + provides the env wiring claude needs.
  Residuals: in-app untrusted_app run owed (same gate shape as Node, passed below);
  embedded-ripgrep extract-to-tmp-and-exec risk (USE_BUILTIN_RIPGREP=0 + ship rg as lib*.so
  if it bites); auth flow + TUI rendering = on-device human gates.

- **2026-07-03 — Node runtime IN-APP VERIFIED (untrusted_app domain): gap #1 + #3 CLOSED.**
  `NodeRuntimeService` + `Native/node/fetch-node-libs.sh` (payloads gitignored, 93 MB/ABI,
  csproj bundles when present — same Exists() pattern as libpty). Logcat proof on emulator:
  `node self-test OK: v26.3.1`, spawned by the app itself from `nativeLibraryDir` via
  first-run symlinks in `filesDir` (`bin/node`, versioned sonames). Env lesson: set BOTH
  environs — `Os.Setenv` (native, feeds forkpty/PTY children) AND
  `Environment.SetEnvironmentVariable` (managed, feeds `Process` children); they don't sync,
  and the first in-app run failed on exactly that. Remaining for Tier 3: frozen
  `node_modules` of Claude Code (incl. vendored-ripgrep exec workaround) + on-device
  ARM64 run + typing `node` in the actual terminal UI.

- **2026-07-03 — Route A (repackage Termux Node) PROBE-VERIFIED on emulator (x86_64, API 36).**
  Termux's `nodejs` 26.3.1 + its 7 dep packages, unpacked from `.deb`s, pushed to
  `/data/local/tmp`, run with `LD_LIBRARY_PATH` pointing at the flat lib dir:
  `node --version`, eval, and a full HTTPS `fetch` to api.anthropic.com all pass
  (DNS/TCP/TLS verified; Node uses its compiled-in Mozilla CA store — no Termux cert
  path involved). Findings: (a) the binary needs versioned sonames (`libz.so.1`,
  `libcrypto.so.3`, `libssl.so.3`, `libsqlite3.so.0`, `libicu*.so.78`) — APKs only
  bundle `lib*.so` names, so create these as first-run symlinks in an app-data dir on
  `LD_LIBRARY_PATH` (same `Os.symlink` mechanism as the planned `bin/node` symlink);
  (b) nodejs-mobile REJECTED (fork stale at Node 18 EOL, JNI-embed shape doesn't fit
  PTY-spawned CLIs); (c) own static build stays fallback-only. Residual: probe ran in
  the `shell` SELinux domain — the same layout inside the APK (`untrusted_app` +
  `nativeLibraryDir`) is the next gate. Also: any freshly compiled `.so` (incl. a
  libpty rebuild) should add `-Wl,-z,max-page-size=16384` — Android 16 wants 16 KB
  pages (build warns XA0141 on the current libpty.so).

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
