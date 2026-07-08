#!/usr/bin/env node
// stpkg — the SmartTerminal package manager.
//
// WHY THIS EXISTS: the phone ships node + bash + rg baked in as lib*.so, but that
// set is frozen at build time. stpkg lets the on-device claude (or the operator)
// pull *additional* tools at runtime — the thing that became possible the moment
// the targetSdk-28 flip made app storage executable ("runtime-exec self-test OK").
//
// HOW IT WORKS: node has https + zlib built in and (since the OPENSSL_CONF fix)
// working TLS, while the phone has no curl/git/python. So the downloader is node
// itself — no bootstrap dependency. A package is fetched over https, laid down
// under <prefix>/opt/<name>/, and its executables are symlinked into <prefix>/bin,
// which NodeRuntimeService already put on PATH. A new shell tab then sees the tool.
//
// SELinux note: exec from app storage only works because targetSdkVersion<=28
// (see AndroidManifest + RuntimeExecSelfTest). On a targetSdk>=29 build every
// installed binary would be unrunnable; stpkg checks nothing about that here — the
// boot self-test is the gate, and this tool assumes it passed.
//
// v0.1 scope: `install` (kind: binary | targz), `list`, `help`. Integrity is
// sha256-pinned when the registry supplies a hash; when it doesn't, stpkg installs
// anyway but PRINTS the computed hash and a loud UNVERIFIED warning, so the pin can
// be added afterward. It never invents a hash it hasn't computed.

'use strict';
const https = require('node:https');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const { execFileSync } = require('node:child_process');

// ---- prefix + dirs -------------------------------------------------------
// HOME is <prefix>/home (set by NodeRuntimeService), so prefix = dirname(HOME).
// STPKG_PREFIX overrides for testing off-device.
const PREFIX = process.env.STPKG_PREFIX ||
  (process.env.HOME ? path.dirname(process.env.HOME) : process.cwd());
const BIN = path.join(PREFIX, 'bin');
const OPT = path.join(PREFIX, 'opt');
const TMP = process.env.TMPDIR || path.join(PREFIX, 'tmp');
const LOG = path.join(TMP, 'stpkg.log');

// ---- registry ------------------------------------------------------------
// kind: 'binary' — the downloaded file IS the executable (rename to bins[0]).
// kind: 'targz'  — gunzip+untar into opt/<name>; symlink each of bins.
// sha256: optional; when present it is enforced.
const REGISTRY = {
  jq: {
    url: 'https://github.com/jqlang/jq/releases/download/jq-1.7.1/jq-linux-arm64',
    kind: 'binary',
    bins: ['jq'],
    // static single binary — the mechanism-proving first target.
  },
  fd: {
    url: 'https://github.com/sharkdp/fd/releases/download/v10.2.0/fd-v10.2.0-aarch64-unknown-linux-musl.tar.gz',
    kind: 'targz',
    bins: ['fd'],
    stripComponents: 1, // tarball has a top-level dir; the binary is one level down
  },
};

// ---- logging -------------------------------------------------------------
function log(msg) {
  const line = `[stpkg] ${msg}`;
  console.log(line);
  try { fs.appendFileSync(LOG, line + '\n'); } catch { /* tmp may be missing off-device */ }
}
function die(msg) { log('ERROR: ' + msg); process.exit(1); }

// ---- download (follows redirects; GitHub assets 302 to objects.github) ---
function download(url, redirectsLeft = 6) {
  return new Promise((resolve, reject) => {
    if (redirectsLeft < 0) return reject(new Error('too many redirects'));
    const req = https.get(url, {
      headers: { 'User-Agent': 'stpkg/0.1', 'Accept': '*/*' },
    }, (res) => {
      const { statusCode, headers } = res;
      if (statusCode >= 300 && statusCode < 400 && headers.location) {
        res.resume(); // drain
        const next = new URL(headers.location, url).toString();
        return resolve(download(next, redirectsLeft - 1));
      }
      if (statusCode !== 200) {
        res.resume();
        return reject(new Error(`HTTP ${statusCode} for ${url}`));
      }
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve(Buffer.concat(chunks)));
      res.on('error', reject);
    });
    req.on('error', reject);
    req.setTimeout(120000, () => req.destroy(new Error('download timeout')));
  });
}

function sha256(buf) { return crypto.createHash('sha256').update(buf).digest('hex'); }

// ---- install -------------------------------------------------------------
async function install(name) {
  const entry = REGISTRY[name];
  if (!entry) die(`unknown package '${name}'. Try: stpkg list`);
  fs.mkdirSync(BIN, { recursive: true });
  fs.mkdirSync(OPT, { recursive: true });

  log(`fetching ${name} <- ${entry.url}`);
  const buf = await download(entry.url);
  const got = sha256(buf);
  if (entry.sha256) {
    if (got !== entry.sha256) die(`sha256 mismatch for ${name}: expected ${entry.sha256}, got ${got}`);
    log(`sha256 OK (${got})`);
  } else {
    log(`UNVERIFIED: no pinned sha256 for ${name}. Computed ${got}`);
    log(`  -> pin it: add sha256:'${got}' to the '${name}' registry entry.`);
  }

  const pkgDir = path.join(OPT, name);
  fs.rmSync(pkgDir, { recursive: true, force: true });
  fs.mkdirSync(pkgDir, { recursive: true });

  if (entry.kind === 'binary') {
    const dest = path.join(pkgDir, entry.bins[0]);
    fs.writeFileSync(dest, buf);
    fs.chmodSync(dest, 0o755);
    linkBin(entry.bins[0], dest);
  } else if (entry.kind === 'targz') {
    const tar = zlib.gunzipSync(buf);
    const tarPath = path.join(TMP, `${name}.tar`);
    fs.writeFileSync(tarPath, tar);
    // toybox `tar` ships in /system/bin on Android — use it rather than a JS untar.
    const args = ['-xf', tarPath, '-C', pkgDir];
    if (entry.stripComponents) args.push(`--strip-components=${entry.stripComponents}`);
    try { execFileSync('tar', args, { stdio: 'pipe' }); }
    catch (e) { die(`tar extract failed (${e.message}). Is 'tar' on PATH?`); }
    fs.rmSync(tarPath, { force: true });
    for (const b of entry.bins) {
      const found = findFile(pkgDir, b);
      if (!found) die(`binary '${b}' not found in ${name} tarball`);
      fs.chmodSync(found, 0o755);
      linkBin(b, found);
    }
  } else {
    die(`unknown kind '${entry.kind}' for ${name}`);
  }
  log(`installed ${name}: ${entry.bins.map((b) => path.join(BIN, b)).join(', ')}`);
  log(`run a NEW shell tab (or 'hash -r') so PATH picks up ${BIN}`);
}

function linkBin(binName, target) {
  const link = path.join(BIN, binName);
  try { fs.unlinkSync(link); } catch { /* absent */ }
  fs.symlinkSync(target, link);
  log(`symlink ${link} -> ${target}`);
}

// shallow recursive search for a filename under dir
function findFile(dir, name) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { const f = findFile(p, name); if (f) return f; }
    else if (e.name === name) return p;
  }
  return null;
}

// ---- cli -----------------------------------------------------------------
function usage() {
  console.log(`stpkg 0.1 — SmartTerminal package manager
  stpkg install <name>   download + install a package
  stpkg list             show the registry
  stpkg help             this text
prefix: ${PREFIX}`);
}

async function main() {
  const [cmd, arg] = process.argv.slice(2);
  switch (cmd) {
    case 'install':
      if (!arg) die('usage: stpkg install <name>');
      await install(arg);
      break;
    case 'list':
      for (const [n, e] of Object.entries(REGISTRY))
        console.log(`  ${n.padEnd(10)} ${e.kind.padEnd(7)} ${e.url}`);
      break;
    case 'help': case undefined: usage(); break;
    default: die(`unknown command '${cmd}'. Try: stpkg help`);
  }
}
main().catch((e) => die(e.message));
