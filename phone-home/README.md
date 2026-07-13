# phone-home/ — files installed into the app's files/home on the device

Installed 2026-07-07 via adb (`run-as com.holger.smartterminal`), NOT shipped
in the APK. Re-install after a data wipe:

    adb push <file> /data/local/tmp/<f>
    adb shell run-as com.holger.smartterminal sh -c 'cat /data/local/tmp/<f> > files/home/<f>'

- `CLAUDE.md` — environment briefing Claude Code reads at session start
  (installed tools, pipe-SIGSYS trap, no-git-yet patterns, tcat usage, pinned-version rule).
- `git.cjs` — git-shaped CLI over isomorphic-git (ROADMAP P2 Route 1): status/add/
  commit/log/clone/pull/push/fetch/branch/checkout/rev-parse/remote/diff(--name-only).
  Unimplemented commands fail LOUD (exit 2); push results are inspected so a rejected
  push can't print success. Needs `isogit.umd.js` + `bus-http.cjs` beside it. On-device:
  install a `files/bin/git` sh wrapper exec'ing node BY ABSOLUTE PATH at this file
  (header of git.cjs has the exact 2 lines). Auth: `~/.gh-token` (0600) or `$GH_TOKEN`;
  author: `~/.gitconfig` `[user]` block.
- `git-selftest.cjs` — hermetic local-plumbing test for git.cjs (27 checks incl. a
  cross-check of `status --porcelain` against real git when present). Laptop AND phone.
- `git-selftest-net.cjs` — transport test: clone/push/pull/fetch + LOUD non-ff push
  rejection against a local `git http-backend` smart-http server. Laptop/desktop only
  (needs real git); run before shipping any git.cjs change.
- `tcat.js` — node port of tools/tcat (python absent on device); OSC 1338
  inline-media emitter. Verified rendering mermaid on-device 2026-07-07.
- `demo.mmd` — first diagram ever rendered in the terminal.

Candidate future home: ship these as MauiAssets extracted by SetupClaude,
so they survive wipes and reach the phone without a cable.
