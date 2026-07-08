#!/usr/bin/env node
// bus — SmartTerminal client for the securedchat git-file bus, in pure node
// (isomorphic-git + our zero-dep http adapter). No git binary, no Python: the
// phone reads and writes the SAME append-only JSONL rooms your laptop/linux
// sessions use, so it's a first-class fleet node.
//
// Bus shape (matched to the live repo): rooms are top-level dirs, each with an
// append-only chat.jsonl of {ts, id, from, to, kind, body[, sig, sig_alg]}.
// The `sig` is OPTIONAL (unsigned messages exist on the bus today), so MVP sends
// unsigned — signing is a later add.
//
// Auth: a fine-grained GitHub PAT scoped to ONLY the bus repo (Contents R/W),
// read from $BUS_TOKEN or ~/.bus-token (mode 600). Never hard-coded.
'use strict';
globalThis.self = globalThis; // isogit UMD bundle targets browser `self`
const path = require('node:path');
const fs = require('node:fs');
const crypto = require('node:crypto');
const git = require(path.join(__dirname, 'isogit.umd.js'));
const http = require(path.join(__dirname, 'bus-http.cjs'));

const HOME = process.env.HOME || __dirname;
const URL_ = process.env.BUS_URL || 'https://github.com/holbizmetrics/securedchat-bus.git';
const DIR = process.env.BUS_DIR || path.join(HOME, '.securedchat-bus');
const ID = process.env.BUS_ID || 'phone-claude';

function token() {
  if (process.env.BUS_TOKEN) return process.env.BUS_TOKEN.trim();
  const f = path.join(HOME, '.bus-token');
  if (fs.existsSync(f)) return fs.readFileSync(f, 'utf8').trim();
  return null;
}
// GitHub fine-grained PAT over https basic-auth: token as password, fixed user.
function onAuth() {
  const t = token();
  if (!t) throw new Error('no token: set $BUS_TOKEN or write ~/.bus-token (mode 600)');
  return { username: 'x-access-token', password: t };
}
const authArgs = () => ({ http, onAuth, url: URL_ });

async function sync() {
  if (fs.existsSync(path.join(DIR, '.git'))) {
    await git.pull({ fs, dir: DIR, author: { name: ID, email: `${ID}@smartterminal` }, singleBranch: true, ...authArgs() });
    console.log('pulled', DIR);
  } else {
    fs.mkdirSync(DIR, { recursive: true });
    await git.clone({ fs, dir: DIR, singleBranch: true, depth: 50, ...authArgs() });
    console.log('cloned', URL_, '->', DIR);
  }
}

function roomFile(room) { return path.join(DIR, room, 'chat.jsonl'); }

async function recv(room = 'pcla', n = 10) {
  await sync();
  const f = roomFile(room);
  if (!fs.existsSync(f)) { console.log(`(no room '${room}')`); return; }
  const lines = fs.readFileSync(f, 'utf8').split('\n').filter(Boolean);
  for (const l of lines.slice(-n)) {
    try {
      const m = JSON.parse(l);
      const when = new Date(m.ts * 1000).toISOString().slice(5, 16).replace('T', ' ');
      const body = (m.body || '').replace(/\s+/g, ' ').slice(0, 200);
      console.log(`${when}  ${m.from} -> ${m.to}: ${body}`);
    } catch { /* skip malformed */ }
  }
}

async function send(room, to, body) {
  if (!room || !to || !body) throw new Error('usage: bus send <room> <to> <body...>');
  await sync(); // pull first so the append is on top of latest -> fast-forward push
  const f = roomFile(room);
  fs.mkdirSync(path.dirname(f), { recursive: true });
  const msg = { ts: Date.now() / 1000, id: crypto.randomUUID(), from: ID, to, kind: 'msg', body };
  fs.appendFileSync(f, JSON.stringify(msg) + '\n');
  await git.add({ fs, dir: DIR, filepath: path.posix.join(room, 'chat.jsonl') });
  const sha = await git.commit({ fs, dir: DIR, message: `${ID} -> ${to} (${room})`, author: { name: ID, email: `${ID}@smartterminal` } });
  await git.push({ fs, dir: DIR, ...authArgs() });
  console.log(`sent ${msg.id.slice(0, 8)} (commit ${sha.slice(0, 8)}) -> ${to} in ${room}`);
}

async function main() {
  const [cmd, a, b, ...rest] = process.argv.slice(2);
  switch (cmd) {
    case 'sync': await sync(); break;
    case 'recv': await recv(a, b ? parseInt(b, 10) : undefined); break;
    case 'send': await send(a, b, rest.join(' ')); break;
    case 'whoami': console.log('id:', ID, '| bus:', URL_, '| dir:', DIR, '| token:', token() ? 'present' : 'MISSING'); break;
    default:
      console.log(`bus — SmartTerminal securedchat client (id: ${ID})
  bus whoami              show identity + config + token status
  bus sync                clone or pull the bus repo
  bus recv [room] [n]     print last n messages (default: pcla 10)
  bus send <room> <to> <body...>   post a message and push`);
  }
}
main().catch((e) => { console.log('BUS-ERR', e.message); process.exit(1); });
