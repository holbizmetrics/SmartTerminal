# phone-home/ — files installed into the app's files/home on the device

Installed 2026-07-07 via adb (`run-as com.holger.smartterminal`), NOT shipped
in the APK. Re-install after a data wipe:

    adb push <file> /data/local/tmp/<f>
    adb shell run-as com.holger.smartterminal sh -c 'cat /data/local/tmp/<f> > files/home/<f>'

- `CLAUDE.md` — environment briefing Claude Code reads at session start
  (installed tools, pipe-SIGSYS trap, no-git-yet patterns, tcat usage, pinned-version rule).
- `tcat.js` — node port of tools/tcat (python absent on device); OSC 1338
  inline-media emitter. Verified rendering mermaid on-device 2026-07-07.
- `demo.mmd` — first diagram ever rendered in the terminal.

Candidate future home: ship these as MauiAssets extracted by SetupClaude,
so they survive wipes and reach the phone without a cable.
