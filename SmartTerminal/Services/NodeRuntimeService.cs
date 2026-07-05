using System.Diagnostics;
using System.IO.Compression;

namespace SmartTerminal.Services;

/// <summary>
/// Wires the bundled Node.js runtime (repackaged Termux build, shipped as lib*.so
/// beside libpty.so) into the shell environment.
///
/// Android constraints this dance satisfies (see ROADMAP "Load-bearing platform constraints"):
///  - Binaries can only exec from nativeLibraryDir, and only lib*.so names get there,
///    so node ships as libnode.so.
///  - The ELF wants versioned sonames (libz.so.1, libcrypto.so.3, ...) which are not
///    APK-legal names, so first-run symlinks in filesDir/lib provide them. The symlink
///    TARGETS live in exec/load-allowed nativeLibraryDir, which satisfies SELinux.
///  - nativeLibraryDir is read-only, so HOME/TMPDIR/npm caches point at filesDir.
///
/// Call Setup() once at startup, BEFORE any PTY spawns. pty.c preserves HOME/PATH
/// when already set, so Os.Setenv here flows into every shell.
/// </summary>
public static class NodeRuntimeService
{
    private const string Tag = "SmartTerminal.Node";

    /// <summary>Versioned soname -> canonical APK lib name.</summary>
    private static readonly (string Link, string Target)[] SonameLinks =
    {
        ("libz.so.1",         "libz.so"),
        ("libcrypto.so.3",    "libcrypto.so"),
        ("libssl.so.3",       "libssl.so"),
        ("libsqlite3.so.0",   "libsqlite3.so"),
        ("libicudata.so.78",  "libicudata.so"),
        ("libicui18n.so.78",  "libicui18n.so"),
        ("libicuuc.so.78",    "libicuuc.so"),
    };

    public static bool NodeAvailable { get; private set; }

    public static void Setup()
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            string nativeDir = ctx.ApplicationInfo!.NativeLibraryDir!;
            string filesDir = ctx.FilesDir!.AbsolutePath;

            if (!File.Exists(Path.Combine(nativeDir, "libnode.so")))
            {
                Android.Util.Log.Info(Tag, "libnode.so not bundled — node runtime disabled.");
                NodeAvailable = false;
                return;
            }

            string binDir = Path.Combine(filesDir, "bin");
            string libDir = Path.Combine(filesDir, "lib");
            string homeDir = Path.Combine(filesDir, "home");
            string tmpDir = Path.Combine(filesDir, "tmp");
            foreach (var d in new[] { binDir, libDir, homeDir, tmpDir })
                Directory.CreateDirectory(d);

            // bin/node -> nativeLibraryDir/libnode.so (argv[0] must be "node", not "libnode.so")
            EnsureSymlink(Path.Combine(binDir, "node"), Path.Combine(nativeDir, "libnode.so"));

            // Versioned sonames the ELF headers reference.
            foreach (var (link, target) in SonameLinks)
                EnsureSymlink(Path.Combine(libDir, link), Path.Combine(nativeDir, target));

            // Environment for every child the PTY forks (pty.c preserves pre-set HOME/PATH).
            // Set BOTH environs: Os.Setenv hits the native environ (inherited by forkpty
            // children); Environment.SetEnvironmentVariable hits the managed snapshot
            // (used by System.Diagnostics.Process children). They do not sync themselves.
            string path = Android.Systems.Os.Getenv("PATH") ?? "/system/bin";
            var env = new Dictionary<string, string>
            {
                ["PATH"] = $"{binDir}:{path}",
                ["LD_LIBRARY_PATH"] = $"{nativeDir}:{libDir}",
                ["HOME"] = homeDir,
                ["TMPDIR"] = tmpDir,
            };
            foreach (var (k, v) in env)
            {
                Android.Systems.Os.Setenv(k, v, true);
                Environment.SetEnvironmentVariable(k, v);
            }

            NodeAvailable = SelfTest(Path.Combine(binDir, "node"));

            SetupClaude(filesDir, homeDir);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error(Tag, $"Node runtime setup failed: {ex}");
            NodeAvailable = false;
        }
#endif
    }

    public static bool ClaudeAvailable { get; private set; }

#if ANDROID
    /// <summary>
    /// Claude Code 2.1.112 — the last PURE-JAVASCRIPT release (2.1.113+ became a thin
    /// wrapper around a compiled musl binary). Running cli.js on the bundled BIONIC Node
    /// means DNS resolves via Android's netd — musl cannot (no /etc/resolv.conf on
    /// Android) — and bionic Node is seccomp-allowlisted, so no SIGSYS shim is needed.
    /// cli.js ships as a MauiAsset (claude-js.zip), extracted to filesDir on first run.
    /// (The musl-binary + shim path is the documented fallback; see ROADMAP 2026-07-05.)
    /// </summary>
    private static void SetupClaude(string filesDir, string homeDir)
    {
        if (!NodeAvailable)
        {
            Android.Util.Log.Info(Tag, "node unavailable — claude-js disabled.");
            ClaudeAvailable = false;
            return;
        }

        string nodePath = Path.Combine(filesDir, "bin", "node");
        string jsDir = Path.Combine(filesDir, "claude-js");
        string cliJs = Path.Combine(jsDir, "cli.js");

        if (!File.Exists(cliJs) && !ExtractClaudeJs(jsDir))
        {
            ClaudeAvailable = false;
            return;
        }

        string rc = Path.Combine(homeDir, ".mkshrc");
        // Start in HOME (app cwd "/" is unlistable for untrusted_app). USE_BUILTIN_RIPGREP=0:
        // the vendored rg can't exec from filesDir (SELinux W^X) — search degrades until rg
        // ships as a native lib (ROADMAP task). claude = node cli.js on the bionic runtime.
        string rcBody = $"cd \"$HOME\"\nalias claude='USE_BUILTIN_RIPGREP=0 {nodePath} {cliJs}'\n";
        if (!File.Exists(rc) || File.ReadAllText(rc) != rcBody)
            File.WriteAllText(rc, rcBody);
        Android.Systems.Os.Setenv("ENV", rc, true);
        Environment.SetEnvironmentVariable("ENV", rc);

        ClaudeAvailable = ClaudeSelfTest(nodePath, cliJs);
    }

    private static bool ExtractClaudeJs(string jsDir)
    {
        try
        {
            Directory.CreateDirectory(jsDir);
            using var asset = Microsoft.Maui.Storage.FileSystem
                .OpenAppPackageFileAsync("claude-js.zip").GetAwaiter().GetResult();
            using var zip = new ZipArchive(asset, ZipArchiveMode.Read);
            zip.ExtractToDirectory(jsDir, overwriteFiles: true);
            Android.Util.Log.Info(Tag, "claude-js extracted.");
            return true;
        }
        catch (FileNotFoundException)
        {
            Android.Util.Log.Info(Tag, "claude-js.zip not bundled — claude disabled.");
            return false;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error(Tag, $"claude-js extract failed: {ex.Message}");
            return false;
        }
    }

    private static bool ClaudeSelfTest(string nodePath, string cliJs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = nodePath,
                Arguments = $"{cliJs} --version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["HOME"] = Android.Systems.Os.Getenv("HOME") ?? "";
            psi.EnvironmentVariables["TMPDIR"] = Android.Systems.Os.Getenv("TMPDIR") ?? "";
            psi.EnvironmentVariables["LD_LIBRARY_PATH"] = Android.Systems.Os.Getenv("LD_LIBRARY_PATH") ?? "";
            psi.EnvironmentVariables["USE_BUILTIN_RIPGREP"] = "0";
            using var p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd().Trim();
            string stderr = p.StandardError.ReadToEnd().Trim();
            p.WaitForExit(60_000);

            if (p.ExitCode == 0 && stdout.Length > 0)
            {
                Android.Util.Log.Info(Tag, $"claude self-test OK: {stdout}");
                return true;
            }
            Android.Util.Log.Error(Tag, $"claude self-test FAILED: exit={p.ExitCode} out='{stdout}' err='{stderr}'");
            return false;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error(Tag, $"claude self-test EXCEPTION: {ex.Message}");
            return false;
        }
    }
#endif

#if ANDROID
    private static void EnsureSymlink(string link, string target)
    {
        try { Android.Systems.Os.Remove(link); } catch { /* didn't exist */ }
        Android.Systems.Os.Symlink(target, link);
    }

    /// <summary>
    /// Spawn `node --version` directly (same untrusted_app domain as PTY children)
    /// and log the outcome — the mechanical proof the runtime works on this device.
    /// </summary>
    private static bool SelfTest(string nodePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = nodePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // Belt and braces: the managed env snapshot can lag native setenv.
            psi.EnvironmentVariables["LD_LIBRARY_PATH"] = Android.Systems.Os.Getenv("LD_LIBRARY_PATH") ?? "";
            psi.EnvironmentVariables["HOME"] = Android.Systems.Os.Getenv("HOME") ?? "";
            psi.EnvironmentVariables["TMPDIR"] = Android.Systems.Os.Getenv("TMPDIR") ?? "";
            using var p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd().Trim();
            string stderr = p.StandardError.ReadToEnd().Trim();
            p.WaitForExit(10_000);

            if (p.ExitCode == 0 && stdout.StartsWith('v'))
            {
                Android.Util.Log.Info(Tag, $"node self-test OK: {stdout}");
                return true;
            }
            Android.Util.Log.Error(Tag, $"node self-test FAILED: exit={p.ExitCode} out='{stdout}' err='{stderr}'");
            return false;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error(Tag, $"node self-test EXCEPTION: {ex.Message}");
            return false;
        }
    }
#endif
}
