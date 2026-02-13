# Claude Code Launcher

A .NET MAUI Android app that makes running Claude Code on your phone a breeze — with **SwiftKey/predictive keyboard support** that Termux lacks.

## 🎯 What This Solves

1. **SwiftKey keyboard doesn't work in Termux** — Predictive keyboards send `commitText()` instead of key events. This app intercepts and handles that properly.
2. **Termux setup is confusing** — One-tap setup wizard installs everything.
3. **Switching apps is clunky** — Quick launch buttons and input controls.

## 🏗️ Architecture

```
ClaudeCodeLauncher/
├── MauiProgram.cs              # App startup, handler registration
├── App.xaml/.cs                # App shell, navigation
├── MainPage.xaml/.cs           # Main UI with launch controls
├── SetupPage.xaml/.cs          # First-time setup wizard
├── Services/
│   ├── ITermuxService.cs       # Termux interaction interface
│   ├── TermuxService.cs        # Cross-platform shell
│   ├── IPreferencesService.cs  # Settings interface
│   └── PreferencesService.cs   # MAUI Preferences wrapper
├── Views/
│   └── SmartInput.cs           # Cross-platform smart input control
├── Platforms/Android/
│   ├── MainActivity.cs
│   ├── MainApplication.cs
│   ├── AndroidManifest.xml     # Termux permissions
│   ├── Services/
│   │   └── TermuxService.Android.cs    # Android Intent implementation
│   └── Handlers/
│       ├── SmartInputConnection.cs     # 🔑 THE FIX for SwiftKey
│       ├── SmartInputView.cs           # Custom Android View
│       └── SmartInputHandler.cs        # MAUI Handler mapping
└── Resources/
    ├── Styles/
    │   ├── Colors.xaml
    │   └── Styles.xaml
    └── Images/
        ├── appicon.svg
        └── appiconfg.svg
```

## 🔧 Requirements

- **.NET 10 SDK** (or .NET 9 — just update `TargetFrameworks` in .csproj)
- **Visual Studio 2022** with MAUI workload, or VS Code with MAUI extension
- **Android SDK** (API 24+)
- **Android device or emulator**

## 📦 First-Time Setup

```bash
# 1. Download required fonts (OpenSans)
chmod +x download-fonts.sh
./download-fonts.sh

# 2. Restore packages
dotnet restore

# 3. Build
dotnet build -f net10.0-android
```

## 🚀 Build & Run

### Option 1: Visual Studio 2022

1. Open `ClaudeCodeLauncher.sln`
2. Select Android device/emulator
3. Press F5

### Option 2: Command Line

```bash
# Restore packages
dotnet restore

# Build for Android
dotnet build -f net10.0-android

# Run on connected device
dotnet build -f net10.0-android -t:Run
```

### Option 3: Create APK

```bash
# Debug APK
dotnet build -f net10.0-android -c Release

# Signed release APK (requires keystore)
dotnet publish -f net10.0-android -c Release
```

## 🔑 Key Innovation: SmartInputConnection

The magic is in `Platforms/Android/Handlers/SmartInputConnection.cs`:

```csharp
public override bool CommitText(ICharSequence? text, int newCursorPosition)
{
    // SwiftKey sends full words here — Termux drops this!
    // We capture it and send to the terminal.
    _onTextCommitted?.Invoke(text?.ToString() ?? "");
    return true;
}
```

Standard terminals expect `sendKeyEvent()` for each character. Predictive keyboards call `commitText()` with whole words. This custom InputConnection handles both.

## 📱 How It Works

1. **Setup Page**: Checks requirements, runs auto-install via Termux intents
2. **Main Page**: One-tap Claude Code launch, command input, special key buttons
3. **SmartInput Control**: Custom MAUI control with proper IME handling
4. **TermuxService**: Sends commands via `com.termux.RUN_COMMAND` intents

## ⚠️ Android 12+ Notes

Android 12 introduced the Phantom Process Killer that can terminate Termux background processes.

**Fix:**
1. Enable Developer Options
2. Find "Disable child process restrictions" 
3. Turn it ON

Or via ADB:
```bash
adb shell device_config set_sync_disabled_for_tests persistent
adb shell device_config put activity_manager max_phantom_processes 2147483647
```

## 📦 Dependencies

- `Microsoft.Maui.Controls` — UI framework
- `CommunityToolkit.Maui` — Extra controls and behaviors
- `CommunityToolkit.Mvvm` — MVVM helpers

## 🛠️ Customization

### Change Target Framework

Edit `ClaudeCodeLauncher.csproj`:
```xml
<TargetFrameworks>net9.0-android</TargetFrameworks>  <!-- For .NET 9 -->
```

### Add iOS Support (theoretically)

This app is Android-specific (Termux is Android-only), but if you wanted iOS:
```xml
<TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>
```
You'd need a different terminal backend for iOS.

## 📄 License

MIT — do whatever you want with it.

## 🤝 Credits

- Built for the Claude Code CLI by Anthropic
- Inspired by Termux's excellent work (and its keyboard limitations)
- SwiftKey fix based on Android InputConnection documentation

---

**Made with ☕ and MAUI by Holger**
