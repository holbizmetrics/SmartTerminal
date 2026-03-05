# Smart Terminal

A .NET 10 MAUI Android terminal emulator with full predictive keyboard support (SwiftKey, Gboard, etc.), inline rich rendering (LaTeX/Markdown), and an extra keys toolbar optimized for developer tools like Claude Code.

## The Problem

Android terminal apps use raw `InputConnection` that drops predictive keyboard input. SwiftKey sends completed words via `commitText()` — standard terminals ignore this and only handle `sendKeyEvent()`. Result: predictive typing is broken in every terminal on Android. On top of that, soft keyboards don't expose Ctrl, Alt, Esc, Tab, or arrow keys — making tools like vim, Claude Code, and package managers unusable.

## The Fix

Smart Terminal intercepts `commitText()` with a custom `SmartInputConnection` and forwards everything to the terminal. SwiftKey works. Gboard works. Swipe typing works. A scrollable extra keys toolbar provides Esc, Ctrl, Alt, Tab, arrows, and common symbols — with sticky modifier support for Ctrl+C, Alt+key combos, etc.

## Features

- **Predictive keyboard support** — SwiftKey, Gboard, swipe typing all work
- **Extra keys toolbar** — Esc, Ctrl (sticky), Alt (sticky), Tab, |, -, ~, /, \, _, arrows, Paste
- **Clipboard integration** — Auto-copy on selection, paste button with bracketed paste mode
- **Rich rendering** — Inline LaTeX (KaTeX) and Markdown via OSC 1337 escape sequences
- **Full terminal emulation** — xterm.js with canvas rendering, 10K scrollback, web links
- **Native PTY** — Real forkpty() via C library, signal-safe I/O with EINTR handling
- **Offline-ready** — All assets bundled locally (xterm.js, KaTeX with fonts, marked.js)
- **CI/CD** — GitHub Actions pipeline builds APK on every push

## Architecture

```
┌─────────────────────────────────────────────┐
│  ExtraKeysBar (horizontal scrollable)       │
│  Esc│Ctrl│Alt│Tab│|│-│~│/│\│_│↑│↓│←│→│Paste│
├─────────────────────────────────────────────┤
│  SmartInputEditText (1x1 pixel, captures KB) │
│  └── SmartInputConnection                    │
│      ├── commitText() → catches SwiftKey     │
│      ├── sendKeyEvent() → catches physical   │
│      └── deleteText() → catches backspace    │
├──────────────┬──────────────────────────────┤
│              ▼                               │
│  WebView (xterm.js + KaTeX + marked.js)      │
│  Terminal emulation + rich rendering:        │
│  colors, cursor, vim, LaTeX, Markdown        │
├──────────────┬──────────────────────────────┤
│              ▼                               │
│  PtyService (C#)                             │
│  └── P/Invoke → libpty.so (native C)         │
│      ├── forkpty() → allocate PTY            │
│      ├── read/write → shell I/O (EINTR-safe) │
│      └── resize → TIOCSWINSZ                 │
├──────────────┬──────────────────────────────┤
│              ▼                               │
│  /bin/sh → node → claude-code                │
└─────────────────────────────────────────────┘
```

## Data Flow

```
SwiftKey → commitText("hello") → SmartInputConnection
    → ExtraKeysBar.ApplyModifiers() (Ctrl/Alt if active)
    → C# InputReceived event
    → PtyService.WriteAsync("hello")
    → libpty.so pty_write()
    → shell stdin
    → shell stdout
    → libpty.so pty_read()
    → PtyService.OutputReceived
    → WebView.EvaluateJavascript("termWrite(base64)")
    → xterm.js renders output

Rich rendering (OSC 1337):
    Program → stdout: ESC ] 1337 ; latex=BASE64 ST
    → xterm.js OSC parser → KaTeX render → Decoration overlay
    Program → stdout: ESC ] 1337 ; markdown=BASE64 ST
    → xterm.js OSC parser → marked.js render → Decoration overlay
```

## Extra Keys Toolbar

The toolbar sits above the soft keyboard with scrollable buttons:

| Key | Action |
|-----|--------|
| ESC | Send `\x1b` |
| CTR | Sticky Ctrl modifier (tap = one-shot, double-tap = locked) |
| ALT | Sticky Alt modifier (tap = one-shot, double-tap = locked) |
| TAB | Send `\t` (shell completion) |
| Arrows | Send ANSI escape sequences |
| PST | Paste from system clipboard (bracketed paste mode) |
| Symbols | `\|` `-` `~` `/` `\` `_` sent as literal characters |

**Modifier states:** OFF (default dark) → ACTIVE (red, one-shot) → LOCKED (cyan, stays on)

## Rich Rendering

Programs can render LaTeX and Markdown inline using OSC 1337 escape sequences:

```bash
# Source the helper script
source smartterm-helpers.sh

# Render LaTeX
latex "E = mc^2"
latex -d "\int_0^\infty e^{-x} dx = 1"   # Display mode (centered)

# Render Markdown
md "# Hello World"
md -l "The formula $E=mc^2$ is famous"    # Markdown with LaTeX

# Render a Markdown file
mdfile README.md
```

## Project Structure

```
SmartTerminal/
├── SmartTerminal.csproj          # .NET 10 MAUI project
├── MauiProgram.cs                # DI, handler registration
├── App.cs                        # App entry (constructor injection)
│
├── Services/
│   ├── IPtyService.cs            # PTY interface
│   └── PtyService.cs             # PTY implementation (P/Invoke)
│
├── Views/
│   ├── SmartTerminalView.cs      # Cross-platform terminal control
│   └── TerminalPage.cs           # Main page (wires view ↔ PTY)
│
├── Platforms/Android/
│   ├── MainActivity.cs
│   ├── MainApplication.cs
│   ├── AndroidManifest.xml
│   ├── Handlers/
│   │   ├── SmartTerminalHandler.cs   # WebView + SmartInputConnection + paste
│   │   └── ExtraKeysBar.cs           # Extra keys toolbar widget
│   └── Resources/xml/
│       └── network_security_config.xml
│
├── Native/
│   ├── pty.c                     # Native PTY wrapper (C, EINTR-safe)
│   ├── build_native.sh           # NDK build script
│   └── libs/                     # Built .so files (per ABI)
│       ├── arm64-v8a/libpty.so
│       ├── armeabi-v7a/libpty.so
│       └── x86_64/libpty.so
│
└── wwwroot/
    ├── terminal.html             # xterm.js + KaTeX + marked.js terminal page
    ├── smartterm-helpers.sh       # CLI helpers (latex, md, mdfile)
    └── vendor/                   # Bundled dependencies (offline)
        ├── xterm/                # xterm.js + addons
        ├── katex/                # KaTeX + fonts (19 woff2 files)
        └── marked/               # marked.js
```

## Build

### Prerequisites

- .NET 10 SDK with MAUI workload
- Android NDK (for native library)
- Android SDK (API 26+)

### Step 1: Build native library

```bash
cd Native
export ANDROID_NDK_HOME=/path/to/ndk
chmod +x build_native.sh
./build_native.sh
# Copies libpty.so into libs/ for each ABI
```

### Step 2: Build and deploy

```bash
dotnet restore
dotnet build -f net10.0-android
dotnet build -f net10.0-android -t:Run   # Deploy to connected device
```

### Or in Visual Studio 2022

1. Open SmartTerminal.csproj
2. Select Android target device
3. F5

### CI/CD

The project includes a GitHub Actions pipeline (`.github/workflows/build-android.yml`) that builds the APK on every push and PR to `main`. APK artifacts are uploaded for download.

To create a release, use the release workflow which can be triggered manually from the GitHub Actions tab with a version tag.

## Roadmap

- **Phase 1** (complete): Shell + xterm.js + SwiftKey fix + rich rendering + extra keys + clipboard
- **Phase 2**: proot-distro integration for Linux environment
- **Phase 3**: apt/pkg package management, multi-tab support
- **Phase 4**: Node.js, Python, etc. — full dev environment

The PTY layer already supports any shell, so swapping `/system/bin/sh` for a proot bash session is a configuration change, not an architecture change.

## Known Constraints

- **Single PTY session**: `PtyService` is registered as a singleton with mutable state. For multi-tab support (Phase 2+), refactor to scoped/factory pattern so each tab gets its own PTY instance.
- **Rich rendering (KaTeX/Markdown)**: Uses OSC 1337 escape sequences. Programs must opt in — no automatic detection of LaTeX/Markdown in regular output.
