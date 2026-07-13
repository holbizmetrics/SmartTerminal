#!/usr/bin/env node
// git — git-shaped CLI for the phone, in pure node (ROADMAP P2 Route 1).
//
// Claude Code discovers git by shelling out; this implements the subset it
// actually calls — status/add/commit/log/pull/push/clone/branch/checkout/
// rev-parse/fetch/remote/diff(file-level) — over isomorphic-git (standard
// .git format, fully compatible with real git later). Same substrate the bus
// client proved on-device: vendored isogit.umd.js + zero-dep bus-http.cjs.
//
// Design rules (from the 2026-07-13 field-test analysis):
//   - Unimplemented commands FAIL LOUD ("not implemented on phone"), exit 2 —
//     never silently no-op (silent failure misled both participants live).
//   - Push results are INSPECTED (a rejected push must not print success —
//     same class as bus.cjs fix 0a75c28).
//   - Full clones only when asked (--all); default singleBranch — pushing from
//     a SHALLOW clone is a known isomorphic-git trap, so no `depth` ever.
//
// Auth (https remotes): fine-grained GitHub PAT from $GH_TOKEN / $GITHUB_TOKEN
// or ~/.gh-token (mode 600). Author: repo .git/config, then ~/.gitconfig, then
// $GIT_AUTHOR_NAME/$GIT_AUTHOR_EMAIL — fail loud with instructions if absent.
//
// On-device install: this file lives in files/home next to isogit.umd.js and
// bus-http.cjs; files/bin/git is a 2-line sh wrapper exec'ing node BY ABSOLUTE
// PATH (bare `node` is unreliable on-device — stpkg convention):
//   #!/system/bin/sh
//   exec /data/data/com.holger.smartterminal/files/bin/node \
//        /data/data/com.holger.smartterminal/files/home/git.cjs "$@"
'use strict';
globalThis.self = globalThis; // isogit UMD bundle targets browser `self`
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');
const git = require(path.join(__dirname, 'isogit.umd.js'));
const http = require(path.join(__dirname, 'bus-http.cjs'));

const HOME = process.env.HOME || os.homedir();

// ---------- helpers ----------------------------------------------------------

function fail(msg, code = 1) { console.error('git-on-phone: ' + msg); process.exit(code); }

function notImplemented(cmd, hint) {
  fail(`'${cmd}' not implemented on phone` + (hint ? ` — ${hint}` : '') +
       ' (see SmartTerminal ROADMAP P2; real git arrives with the bionic build, Route 2b)', 2);
}

function token() {
  if (process.env.GH_TOKEN) return process.env.GH_TOKEN.trim();
  if (process.env.GITHUB_TOKEN) return process.env.GITHUB_TOKEN.trim();
  const f = path.join(HOME, '.gh-token');
  if (fs.existsSync(f)) return fs.readFileSync(f, 'utf8').trim();
  return null;
}

function onAuth() {
  const t = token();
  if (!t) throw new Error('no token: set $GH_TOKEN or write ~/.gh-token (mode 600)');
  return { username: 'x-access-token', password: t };
}

// Minimal ~/.gitconfig reader — only [user] name/email, which is all we need.
function globalGitconfig() {
  const f = path.join(HOME, '.gitconfig');
  const out = {};
  if (!fs.existsSync(f)) return out;
  let section = '';
  for (const raw of fs.readFileSync(f, 'utf8').split('\n')) {
    const line = raw.trim();
    const sec = line.match(/^\[([^\]"]+)/);
    if (sec) { section = sec[1].trim().toLowerCase(); continue; }
    const kv = line.match(/^([A-Za-z][A-Za-z0-9]*)\s*=\s*(.*)$/);
    if (kv && section === 'user') out[kv[1].toLowerCase()] = kv[2].trim();
  }
  return out;
}

async function author(dir) {
  const name = await git.getConfig({ fs, dir, path: 'user.name' }).catch(() => null);
  const email = await git.getConfig({ fs, dir, path: 'user.email' }).catch(() => null);
  const g = globalGitconfig();
  const a = {
    name: name || g.name || process.env.GIT_AUTHOR_NAME,
    email: email || g.email || process.env.GIT_AUTHOR_EMAIL,
  };
  if (!a.name || !a.email) {
    fail('author unknown — write ~/.gitconfig with\n' +
         '  [user]\n    name = Your Name\n    email = you@example.com');
  }
  return a;
}

// Find the repo root walking up from cwd (or -C dir); fail loud like real git.
async function repoDir(startDir) {
  try {
    return await git.findRoot({ fs, filepath: path.resolve(startDir) });
  } catch {
    fail(`not a git repository (or any parent): ${path.resolve(startDir)}`, 128);
  }
}

const posix = (p) => p.split(path.sep).join('/');

// statusMatrix row -> `git status --porcelain` XY code (null = clean, skip).
// Row: [filepath, HEAD(0|1), WORKDIR(0|1|2), STAGE(0..3)]
// X = index vs HEAD, Y = workdir vs index — the diff command reuses the columns.
const PORCELAIN = {
  '020': '??', '022': 'A ', '023': 'AM', '003': 'AD',
  '111': null, '121': ' M', '122': 'M ', '123': 'MM',
  '113': 'MM', // staged change, workdir reverted to HEAD content (verified vs real git)
  '103': 'MD', // staged change, workdir deleted
  '101': ' D', '100': 'D ',
  '110': 'D ', '120': 'D ', // staged deletion with the file still on disk (rm --cached class; approximation)
};
function porcelainCode([, head, workdir, stage]) {
  const code = PORCELAIN[`${head}${workdir}${stage}`];
  // NOTE: explicit undefined check — `?? '??'` would swallow the null (=clean)
  // entries and print every unmodified file as untracked (caught by selftest).
  return code === undefined ? '??' : code;
}

async function stageAll(dir, { trackedOnly = false } = {}) {
  const matrix = await git.statusMatrix({ fs, dir });
  const staged = [];
  for (const [filepath, head, workdir] of matrix) {
    if (trackedOnly && head === 0) continue;      // -a stages tracked files only
    if (workdir === 0 && head === 1) {            // deleted
      await git.remove({ fs, dir, filepath });
      staged.push(filepath);
    } else if (workdir === 2) {                   // new or modified
      await git.add({ fs, dir, filepath });
      staged.push(filepath);
    }
  }
  return staged;
}

// ---------- commands ---------------------------------------------------------

const commands = {
  async version() { console.log(`git version 2.phone.${git.version()} (git.cjs over isomorphic-git)`); },

  async init(dir, args) {
    const target = path.resolve(args[0] || '.');
    fs.mkdirSync(target, { recursive: true });
    await git.init({ fs, dir: target, defaultBranch: 'main' });
    console.log(`Initialized empty Git repository in ${path.join(target, '.git')}`);
  },

  async clone(dir, args) {
    const all = args.includes('--all');
    const rest = args.filter(a => a !== '--all' && !a.startsWith('--'));
    const url = rest[0];
    if (!url) fail('usage: git clone <url> [dir] [--all]');
    const target = path.resolve(rest[1] || url.replace(/\/+$/, '').split('/').pop().replace(/\.git$/, ''));
    if (fs.existsSync(path.join(target, '.git'))) fail(`destination '${target}' already exists and is a repo`);
    fs.mkdirSync(target, { recursive: true });
    // FULL depth always (shallow clones break push in isomorphic-git).
    // singleBranch by default to spare phone bandwidth; --all for every branch.
    await git.clone({ fs, http, dir: target, url, singleBranch: !all, onAuth });
    console.log(`Cloned ${url} -> ${target}` + (all ? '' : '  (default branch only — re-clone with --all for every branch)'));
  },

  async status(dir, args) {
    const matrix = await git.statusMatrix({ fs, dir });
    const lines = [];
    for (const row of matrix) {
      const code = porcelainCode(row);
      if (code) lines.push(`${code} ${row[0]}`);
    }
    if (args.includes('--porcelain') || args.includes('-s') || args.includes('--short')) {
      for (const l of lines) console.log(l);
    } else {
      const branch = await git.currentBranch({ fs, dir, fullname: false }).catch(() => null);
      console.log(`On branch ${branch ?? '(detached HEAD)'}`);
      if (!lines.length) console.log('nothing to commit, working tree clean');
      else { console.log('Changes (porcelain codes):'); for (const l of lines) console.log('  ' + l); }
    }
  },

  async add(dir, args) {
    if (!args.length) fail("usage: git add <path>... | -A | .");
    if (args.includes('-A') || args.includes('--all')) { await stageAll(dir); return; }
    for (const a of args.filter(x => !x.startsWith('-'))) {
      const abs = path.resolve(a);
      const rel = posix(path.relative(dir, abs));
      if (a === '.' || rel === '' || rel === '.') { await stageAll(dir); return; }
      if (fs.existsSync(abs)) {
        if (fs.statSync(abs).isDirectory()) {
          // stage everything under the directory via the matrix filter
          const matrix = await git.statusMatrix({ fs, dir, filter: f => f === rel || f.startsWith(rel + '/') });
          for (const [filepath, head, workdir] of matrix) {
            if (workdir === 0 && head === 1) await git.remove({ fs, dir, filepath });
            else if (workdir === 2) await git.add({ fs, dir, filepath });
          }
        } else await git.add({ fs, dir, filepath: rel });
      } else {
        await git.remove({ fs, dir, filepath: rel }); // real-git behavior: `add` of a deleted path stages the deletion
      }
    }
  },

  async commit(dir, args) {
    let message = null, stageTracked = false;
    for (let i = 0; i < args.length; i++) {
      if (args[i] === '-m') message = args[++i];
      else if (args[i] === '-am') { stageTracked = true; message = args[++i]; }
      else if (args[i] === '-a') stageTracked = true;
      else if (args[i].startsWith('-')) notImplemented(`commit ${args[i]}`, 'only -m / -a / -am');
    }
    if (!message) fail('commit needs -m "<message>" (no editor on phone)');
    if (stageTracked) await stageAll(dir, { trackedOnly: true });
    const oid = await git.commit({ fs, dir, message, author: await author(dir) });
    const branch = await git.currentBranch({ fs, dir, fullname: false }).catch(() => 'HEAD');
    console.log(`[${branch} ${oid.slice(0, 7)}] ${message.split('\n')[0]}`);
  },

  async log(dir, args) {
    let depth = 20, oneline = false;
    for (let i = 0; i < args.length; i++) {
      const a = args[i];
      if (a === '--oneline') oneline = true;
      else if (a === '-n' || a === '--max-count') depth = parseInt(args[++i], 10);
      else if (/^-\d+$/.test(a)) depth = parseInt(a.slice(1), 10);
      else if (a.startsWith('--max-count=')) depth = parseInt(a.split('=')[1], 10);
      else if (a.startsWith('-')) notImplemented(`log ${a}`, 'only --oneline / -n N');
    }
    const entries = await git.log({ fs, dir, depth });
    for (const e of entries) {
      if (oneline) console.log(`${e.oid.slice(0, 7)} ${e.commit.message.split('\n')[0]}`);
      else {
        console.log(`commit ${e.oid}`);
        console.log(`Author: ${e.commit.author.name} <${e.commit.author.email}>`);
        console.log(`Date:   ${new Date(e.commit.author.timestamp * 1000).toISOString()}`);
        console.log('');
        console.log('    ' + e.commit.message.trimEnd().split('\n').join('\n    '));
        console.log('');
      }
    }
  },

  async push(dir, args) {
    const ref = args.find(a => !a.startsWith('-') && a !== 'origin');
    const result = await git.push({ fs, http, dir, remote: 'origin', ref, onAuth });
    // INSPECT the result — a rejected push must not look like success.
    const refErrors = Object.entries(result.refs ?? {})
      .filter(([, r]) => r.error).map(([name, r]) => `${name}: ${r.error}`);
    if (result.ok !== true || result.error || refErrors.length) {
      fail(`push REJECTED — ${result.error ?? refErrors.join('; ') ?? 'unknown refusal'}`);
    }
    console.log('push ok: ' + Object.keys(result.refs ?? {}).join(', '));
  },

  async pull(dir, args) {
    await git.pull({ fs, http, dir, author: await author(dir), singleBranch: true, onAuth });
    const head = await git.resolveRef({ fs, dir, ref: 'HEAD' });
    console.log(`pull ok — HEAD now ${head.slice(0, 7)}`);
  },

  async fetch(dir) {
    const res = await git.fetch({ fs, http, dir, onAuth });
    console.log(`fetch ok — ${res.fetchHead ? 'FETCH_HEAD ' + res.fetchHead.slice(0, 7) : 'up to date'}`);
  },

  async branch(dir, args) {
    const create = args.find(a => !a.startsWith('-'));
    if (create) { await git.branch({ fs, dir, ref: create }); return; }
    const current = await git.currentBranch({ fs, dir, fullname: false }).catch(() => null);
    for (const b of await git.listBranches({ fs, dir })) {
      console.log((b === current ? '* ' : '  ') + b);
    }
  },

  async checkout(dir, args) {
    if (args[0] === '-b') {
      if (!args[1]) fail('usage: git checkout -b <branch>');
      await git.branch({ fs, dir, ref: args[1], checkout: true });
      console.log(`Switched to a new branch '${args[1]}'`);
      return;
    }
    const ref = args.find(a => !a.startsWith('-'));
    if (!ref) fail('usage: git checkout <branch|ref> | -b <branch>');
    await git.checkout({ fs, dir, ref });
    console.log(`Switched to '${ref}'`);
  },

  async 'rev-parse'(dir, args) {
    for (const a of args) {
      if (a === '--show-toplevel') console.log(posix(dir));
      else if (a === '--is-inside-work-tree') console.log('true');
      else if (a === '--abbrev-ref') continue; // handled with the ref that follows
      else if (a === '--short') continue;      // handled with the ref that follows
      else if (a === '--git-dir') console.log(posix(path.join(dir, '.git')));
      else if (a.startsWith('--')) notImplemented(`rev-parse ${a}`);
      else {
        if (args.includes('--abbrev-ref')) {
          console.log(await git.currentBranch({ fs, dir, fullname: false }) ?? 'HEAD');
        } else {
          const oid = await git.resolveRef({ fs, dir, ref: a });
          console.log(args.includes('--short') ? oid.slice(0, 7) : oid);
        }
      }
    }
    if (!args.length) fail('usage: git rev-parse <ref> | --short <ref> | --abbrev-ref HEAD | --show-toplevel');
  },

  async remote(dir, args) {
    const remotes = await git.listRemotes({ fs, dir });
    for (const r of remotes) {
      if (args.includes('-v')) { console.log(`${r.remote}\t${r.url} (fetch)`); console.log(`${r.remote}\t${r.url} (push)`); }
      else console.log(r.remote);
    }
  },

  async diff(dir, args) {
    const cached = args.includes('--cached') || args.includes('--staged');
    if (args.includes('--name-only') || args.includes('--stat')) {
      const matrix = await git.statusMatrix({ fs, dir });
      for (const row of matrix) {
        const code = porcelainCode(row);
        if (!code || code === '??') continue; // clean or untracked — never in a diff
        // default diff = workdir vs index (Y column); --cached = index vs HEAD (X column)
        const changed = cached ? code[0] !== ' ' : code[1] !== ' ';
        if (changed) console.log(row[0]);
      }
      return;
    }
    notImplemented('diff (content)', 'use `git diff --name-only`, or read the files — content diff needs a JS diff lib (ROADMAP P2 known-hard part)');
  },
};

// ---------- main -------------------------------------------------------------

(async () => {
  const argv = process.argv.slice(2);
  let cwd = process.cwd();
  // global flags: -C <dir>, --version
  while (argv.length) {
    if (argv[0] === '-C') { argv.shift(); cwd = path.resolve(cwd, argv.shift() ?? fail('-C needs a dir')); }
    else if (argv[0] === '--version') { argv[0] = 'version'; break; }
    else break;
  }
  const cmd = argv.shift();
  if (!cmd) fail('usage: git [-C dir] <command> [args]   (git-shaped CLI over isomorphic-git — subset only, unimplemented commands fail loud)');
  const fn = commands[cmd];
  if (!fn) notImplemented(cmd);
  // init/clone/version work outside a repo; everything else resolves the root first
  const needsRepo = !['init', 'clone', 'version'].includes(cmd);
  const dir = needsRepo ? await repoDir(cwd) : cwd;
  try {
    await fn(dir, argv);
  } catch (e) {
    fail(`${cmd}: ${e.message ?? e}`);
  }
})();
