# OSC 1338 — Inline Rich Media

**Status:** v0.1 draft, 2026-05-20
**Scope:** SmartTerminal inline media rendering (mermaid, SVG, audio, image)
**Companion:** OSC 1337 (existing — LaTeX / Markdown text rendering)

## Why a new OSC code

OSC 1337 currently handles text-shaped rich content (LaTeX, Markdown) with a 64 KB payload cap. Media payloads (audio, image) have different size profiles and renderer constraints. Keeping media on a separate OSC keeps:

- Size policies independent (text stays bounded; media may grow per type)
- Feature detection clean (a terminal can support 1337 without 1338, or vice versa)
- Renderer logic separated (text overlays vs. media overlays have different lifecycle needs)

OSC 1338 was chosen because it does not collide with iTerm2's reserved 1337 namespace.

## Wire format

```
ESC ] 1338 ; type=<TYPE> ; data=<PAYLOAD> [; <K>=<V>]* ST
```

- `ESC` = `\x1b`, `]` = `\x5d`, `ST` = `\x07` (BEL) or `\x1b\x5c` (ESC `\`)
- Parameters separated by `;`
- `type` is mandatory and MUST come first after `1338;`
- `data` is mandatory and contains the payload (encoding per type — see below)
- Additional parameters per type (see Types)

## Types

| `type=` | Payload encoding | Optional params | Renderer |
|---|---|---|---|
| `mermaid` | base64 of raw mermaid source (UTF-8) | `theme=dark\|light` (default `dark`) | mermaid.js → inline SVG |
| `svg` | base64 of raw SVG XML (UTF-8) | — | sanitized SVG inserted into overlay |
| `audio` | base64 of audio bytes | `mime=audio/mpeg\|audio/ogg\|audio/wav` (default `audio/mpeg`) | HTML5 `<audio controls>` with data: URL |
| `image` | base64 of image bytes | `mime=image/png\|image/jpeg\|image/gif\|image/webp` (default `image/png`); `width=<px>`; `height=<px>` | HTML5 `<img>` with data: URL |

## Size limits

- `mermaid`: 64 KB (matches OSC 1337 text limit; mermaid source is text)
- `svg`: 256 KB (SVG can be vector-dense)
- `audio`: 4 MB (~30s of 128kbps mp3)
- `image`: 2 MB

Payloads exceeding limit MUST be ignored with a console warning. No partial rendering.

## Lifecycle

Rendering follows the existing OSC 1337 Decoration-API pattern:

1. OSC 1338 handler decodes `type` and `data`
2. Creates an xterm.js marker at current cursor line
3. Creates a DOM overlay element (sanitized HTML per type)
4. Registers a Decoration anchored at the marker
5. On `decoration.onRender`, appends the overlay to the cell DOM
6. On `decoration.onDispose` (row scrolled off scrollback), removes from `richOverlays` Map

Decorations and overlays for OSC 1338 share `richOverlays` Map with OSC 1337 — keyed by `marker.id`. Each entry has a `type` field discriminating mermaid/svg/audio/image/latex/markdown.

## Sanitization

- `svg`: parse into temp `<svg>`, strip `script` elements, strip `on*` event handlers, strip `xlink:href` and `href` starting with `javascript:`
- `mermaid`: rendered by mermaid.js which produces SVG — apply the same SVG sanitization to mermaid's output before insertion
- `audio` / `image`: data: URL constructed from declared MIME; no HTML injection vector

## Fallback behavior

A terminal without OSC 1338 support sees the raw escape sequence in its character stream. Standard terminals strip unrecognized OSC sequences silently (per VT100/xterm convention). The user's text output is unaffected — only the rich content fails to render. Tools emitting OSC 1338 SHOULD provide a textual fallback printed before the sequence when running on a non-supporting terminal (the emitter detects via `TERM`, `$SMARTTERM`, or a probe sequence — out of scope for this SPEC).

## Click-to-copy

- `mermaid`: copies the original mermaid source
- `svg`: copies the original SVG XML
- `audio`: copies the data: URL (or a generated filename if persisted)
- `image`: copies the data: URL

Same `.copied` outline animation as OSC 1337 overlays.

## Future (v2)

- `pdf` (heavy — needs pdf.js, deferred)
- `video` (heavy — needs codec support, deferred)
- `iframe` (sandboxing required, deferred)
- `tone` (executable audio code, sandboxing required, deferred)
- Auto-detection of raw mermaid in `cat` output (false-positive risk, deferred)

## References

- iTerm2 OSC 1337 spec: https://iterm2.com/documentation-escape-codes.html
- xterm.js Decoration API: https://xtermjs.org/docs/api/addons/decorations/
- VT100/xterm OSC handling: https://invisible-island.net/xterm/ctlseqs/ctlseqs.html
