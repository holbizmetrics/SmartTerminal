# Smart Terminal

A .NET 10 MAUI Android terminal emulator with full predictive keyboard support (SwiftKey, Gboard, etc.).

## The Problem

Android terminal apps use raw `InputConnection` that drops predictive keyboard input. SwiftKey sends completed words via `commitText()` — standard terminals ignore this and only handle `sendKeyEvent()`. Result: predictive typing is broken in every terminal on Android.

## The Fix

Smart Terminal intercepts `commitText()` with a custom `SmartInputConnection` and forwards everything to the terminal. SwiftKey works. Gboard works. Swipe typing works.

## Architecture

```
┌─────────────────────────────────────────────┐
│  SmartInputEditText (invisible overlay)      │
│  └── SmartInputConnection                    │
│      ├── commitText() → catches SwiftKey     │
│      ├── sendKeyEvent() → catches physical   │
│      └── deleteText() → catches backspace    │
├──────────────┬──────────────────────────────┤
│              ▼                               │
│  WebView (xterm.js)                          │
│  Full terminal emulation:                    │
│  colors, cursor, vim, alternate screen       │
├──────────────┬──────────────────────────────┤
│              ▼                               │
│  PtyService (C#)                             │
│  └── P/Invoke → libpty.so (native C)         │
│      ├── forkpty() → allocate PTY            │
│      ├── read/write → shell I/O              │
│      └── resize → TIOCSWINSZ                 │
├──────────────┬──────────────────────────────┤
│              ▼                               │
│  /bin/sh → node → claude-code                │
└─────────────────────────────────────────────┘
```

## Data Flow

```
SwiftKey → commitText("hello") → SmartInputConnection
    → C# InputReceived event
    → PtyService.WriteAsync("hello")
    → libpty.so pty_write()
    → shell stdin
    → shell stdout
    → libpty.so pty_read()
    → PtyService.OutputReceived
    → WebView.EvaluateJavascript("termWrite(base64)")
    → xterm.js renders output
```

## Project Structure

```
SmartTerminal/
├── SmartTerminal.csproj          # .NET 10 MAUI project
├── MauiProgram.cs                # DI, handler registration
├── App.cs                        # App entry
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
│   │   └── SmartTerminalHandler.cs   # WebView + SmartInputConnection
│   └── Resources/xml/
│       └── network_security_config.xml
│
├── Native/
│   ├── pty.c                     # Native PTY wrapper (C)
│   ├── build_native.sh           # NDK build script
│   └── libs/                     # Built .so files go here
│       ├── arm64-v8a/libpty.so
│       ├── armeabi-v7a/libpty.so
│       └── x86_64/libpty.so
│
└── wwwroot/
    └── terminal.html             # xterm.js terminal page
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

## Future: Package Management

The architecture is designed for extensibility toward full Termux parity:

- **Phase 1** (current): Shell + xterm.js + SwiftKey fix
- **Phase 2**: proot-distro integration for Linux environment
- **Phase 3**: apt/pkg package management
- **Phase 4**: Node.js, Python, etc. — full dev environment

The PTY layer already supports any shell, so swapping `/system/bin/sh` for a proot bash session is a configuration change, not an architecture change.
