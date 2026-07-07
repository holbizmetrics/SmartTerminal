#!/usr/bin/env node
// tcat.js — OSC 1338 inline-media emitter (node port of tools/tcat, for the
// phone where python is absent). SmartTerminal renders the output inline.
// Usage: node ~/tcat.js file.{mmd,svg,png,jpg,gif,webp,mp3,ogg,wav}
//        node ~/tcat.js --type mermaid < diagram.mmd
// Spec: SmartTerminal/docs/OSC-1338-SPEC.md
const fs = require("fs");
const EXT = { ".mmd": "mermaid", ".mermaid": "mermaid", ".svg": "svg",
  ".png": "image", ".jpg": "image", ".jpeg": "image", ".gif": "image",
  ".webp": "image", ".mp3": "audio", ".ogg": "audio", ".wav": "audio" };
const MIME = { ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
  ".gif": "image/gif", ".webp": "image/webp", ".mp3": "audio/mpeg",
  ".ogg": "audio/ogg", ".wav": "audio/wav" };
const LIMITS = { mermaid: 65536, svg: 262144, audio: 4194304, image: 2097152 };

const args = process.argv.slice(2);
let type = null, file = null;
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--type") type = args[++i];
  else file = args[i];
}
let payload, mime = "";
if (file) {
  const ext = (file.match(/\.[^.]+$/) || [""])[0].toLowerCase();
  type = type || EXT[ext];
  if (!type) { console.error(`tcat: unknown extension '${ext}'; pass --type`); process.exit(2); }
  payload = fs.readFileSync(file);
  mime = MIME[ext] || "";
} else {
  if (!type) { console.error("tcat: --type required when reading stdin"); process.exit(2); }
  payload = fs.readFileSync(0);
}
if (!(type in LIMITS)) { console.error(`tcat: unknown type '${type}'`); process.exit(2); }
if (type === "audio" || type === "image") mime = mime || (type === "audio" ? "audio/mpeg" : "image/png");
else mime = "";
if (payload.length > LIMITS[type]) {
  console.error(`tcat: payload ${payload.length} B exceeds ${type} limit ${LIMITS[type]} B`);
  process.exit(1);
}
const params = [`type=${type}`];
if (mime) params.push(`mime=${mime}`);
params.push(`data=${payload.toString("base64")}`);
process.stdout.write(`\x1b]1338;${params.join(";")}\x07\n`);
