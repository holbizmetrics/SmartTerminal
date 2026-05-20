# OSC 1338 — Manual Smoke Tests (until JS automation lands)

Run these on a deployed SmartTerminal build. Each step has an expected visible outcome.

**Prerequisite:** `tools/tcat` is on PATH (or invoked as `python tools/tcat`).
**Fixture dir:** `tools/tests/fixtures/` (create with `mkdir -p tools/tests/fixtures` if absent).

## 1. Mermaid happy path

```bash
echo 'graph TD
  A[Start] --> B{Decision}
  B -->|Yes| C[Done]
  B -->|No| D[Retry]' > /tmp/test.mmd

tcat /tmp/test.mmd
```

**Expect:** a flowchart diagram renders inline above the next prompt. Nodes show A/B/C/D with the right edges. Theme matches terminal palette (magenta border).

## 2. Mermaid invalid source

```bash
echo 'not valid mermaid' | tcat --type mermaid
```

**Expect:** an error overlay reading `Mermaid error: ...` in red. Terminal does NOT crash. Next prompt appears normally.

## 3. SVG happy path

```bash
echo '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="60">
  <circle cx="50" cy="30" r="25" fill="#c77dba"/>
</svg>' | tcat --type svg
```

**Expect:** a magenta circle renders inline.

## 4. SVG with injected script (sanitization)

```bash
echo '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50" onload="alert(1)">
  <script>alert(2)</script>
  <rect width="50" height="50" fill="#53b3cb"/>
</svg>' | tcat --type svg
```

**Expect:** cyan rectangle renders. **No alert dialog fires.** Browser console may log script-removal traces.

## 5. Image (PNG)

```bash
# Use any small PNG on disk
tcat ~/some-image.png
```

**Expect:** image renders inline at native size (capped to overlay max-width).

## 6. Audio (MP3)

```bash
tcat ~/some-audio.mp3
```

**Expect:** HTML5 audio controls render inline. Play button works.

## 7. Size-limit enforcement

```bash
# Create a 70KB mermaid file (mermaid limit is 64KB)
python -c "print('graph TD' + chr(10) + ' A-->B' * 12000)" > /tmp/huge.mmd
tcat /tmp/huge.mmd
```

**Expect:** `tcat` exits with `exceeds mermaid limit` error to stderr; no escape sequence emitted; terminal unchanged.

## 8. Click-to-copy

After any of the above renders, **click the overlay**.

**Expect:** brief outline animation; the source (mermaid text / SVG XML / data URL) is copied to system clipboard. Verify by pasting elsewhere.

## 9. Graceful degradation in a non-SmartTerminal context

Run `tcat /tmp/test.mmd` in a plain bash / zsh / Termux session (not SmartTerminal).

**Expect:** raw escape bytes appear in the output but the terminal does not crash. Some terminals may show garbled chars (BEL beep, brackets); none should error.

## 10. Scroll-off disposal

Render a mermaid diagram, then run enough commands to scroll the diagram past the top of the buffer (off the 10K scrollback).

**Expect:** diagram disappears with its line, overlay removed from `richOverlays` Map (verify with `term._addonManager` or check via dev console — `richOverlays.size` should not grow unbounded).

## Bug-report template

If anything fails, capture: device model, Android version, app version (commit hash), exact `tcat` command, browser console log (`adb logcat -s chromium`), screenshot.

## Automation plan (deferred)

When test budget allows: introduce Playwright + a fixture HTML page that loads `terminal.html` in a real Chromium, then asserts on DOM (the `.media-overlay` element exists with a child `<svg>` after dispatch). The pure-logic parts (`handleOsc1338` dispatch, size limits) can also move to a standalone `osc1338.js` module testable with Node + jsdom.
