# SmartTerminal

Android terminal that hosts real developer CLIs — including Claude Code running
in-app — with full predictive-keyboard support and inline rich media. Built with
.NET MAUI (Android), xterm.js, and a native PTY.

## What it does

- **Real PTY** — `libpty.so` (`forkpty()`), so interactive/TUI programs get raw
  mode, `isatty`, resize (SIGWINCH). Claude Code's full-screen UI runs in-app.
- **Predictive keyboards work** — SwiftKey/Gboard word predictions arrive via
  `commitText()`/composing text, which classic terminals drop. A custom
  `InputConnection` captures both paths (and flushes composing text on Enter,
  so a word mid-prediction is never lost).
- **Multi-tab** — independent PTY sessions with a bottom tab bar.
- **Extra-keys bar** — ESC · TAB · arrows (hold to repeat) · sticky Ctrl/Alt
  (off → one-shot → locked) · paste/copy · symbols · scrollback jump. Daily keys
  first; the bar scrolls for the rest.
- **Touch gestures** — pinch-to-zoom font size (persisted); long-press context
  menu: Paste / Copy screen / Copy all / **Select text** (native Android
  selection handles over the buffer — xterm renders to canvas, so DOM-less
  selection needs this overlay).
- **Smart paste** — clipboard text arrives as a bracketed paste; a clipboard
  *image* is saved to cache and its file path is injected (paste screenshots
  straight into Claude Code).
- **Inline rich media** — OSC 1337 (LaTeX via KaTeX, Markdown via marked) and
  OSC 1338 (mermaid / SVG / image / audio) render inline in the stream.
  Emitter: `tools/tcat`. Spec: `docs/OSC-1338-SPEC.md`.
- **Runtime tooling (`phone-home/`)** — deployed into the app's home dir:
  - `stpkg` — dependency-free package manager: https download → sha256 pin →
    `files/opt/<pkg>` → symlink into `files/bin` (on PATH). Registry targets
    must be **aarch64-musl statics** (glibc dies SIGSYS under app seccomp).
  - `git.cjs` — git-shaped CLI over vendored isomorphic-git (status/add/commit/
    log/clone/pull/push/…); unimplemented verbs fail loud, push results are
    inspected so a rejected push can't print success.
  - `bus.cjs` — SecuredChat git-file-bus client (the phone as a fleet node).

## Architecture

```
MainActivity (adjustResize, keep-screen-on)
  └─ TabbedTerminalPage        one PTY + terminal view per tab
       └─ SmartTerminalView    cross-platform surface
            └─ SmartTerminalHandler (Android)
                 ├─ WebView → wwwroot/terminal.html (xterm.js, canvas)
                 │    JS → C#: console.log("smartterm:type:base64") bridge
                 │    C# → JS: EvaluateJavascript(termWrite/termResize/…)
                 ├─ SmartInputEditText  invisible 1×1 overlay = the ONE IME
                 │    target (the WebView is deliberately non-focusable, so
                 │    keyboard input has a single, predictable path)
                 └─ ExtraKeysBar
       PtyServiceFactory → PtyService (libpty) — pipe-shell fallback exists
```

## Load-bearing platform constraints

1. **`targetSdk 28`** — exec from the app data dir is allowed (SELinux blocks it
   on 29+). This is what makes runtime installs (`stpkg`) possible at all.
2. **musl, not glibc** — static *glibc* binaries install fine and die with
   SIGSYS ("Bad system call") under the app-sandbox seccomp filter. Static
   *musl* aarch64 binaries run. (Registry keeps a flagged glibc specimen: jq.)
3. **WebView canvas rendering** — no DOM text, so Android's native long-press
   selection can't reach terminal output; the select-text overlay provides it.
4. **Bare-command PATH search can SIGSYS the shell** — invoke installed tools
   by absolute path until the shell-side syscall shim lands (ROADMAP P1).

## Building

```bash
dotnet build SmartTerminal/SmartTerminal.csproj -c Debug              # build
dotnet build SmartTerminal/SmartTerminal.csproj -t:Install -c Debug   # deploy (device via adb; force-stops the app)
```

`wwwroot/` (including `terminal.html`) ships as Android assets — JS changes
need a rebuild + reinstall.

## More

- `ROADMAP.md` — shipped state, field-test gaps (P1–P6), decision log.
- `phone-home/CLAUDE.md` — on-device session notes; bus files are load-bearing.
- `docs/` — OSC 1338 spec and design notes.
