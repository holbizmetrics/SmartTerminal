#!/usr/bin/env node
// git-selftest — laptop/desktop verification for git.cjs (no network, no token).
// Exercises the local subset end-to-end in a temp repo and, when a real git
// binary is present, cross-checks `status --porcelain` against it on the same
// fixture (the output contract Claude Code actually parses).
// Run:  node git-selftest.cjs   → exits 0 with "ALL PASS" or 1 with the failure.
'use strict';
const { execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');

const GIT_CJS = path.join(__dirname, 'git.cjs');
const NODE = process.execPath;

let failures = 0;
function check(name, actual, expected) {
  const ok = typeof expected === 'function' ? expected(actual) : actual === expected;
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}` + (ok ? '' : `\n      got:      ${JSON.stringify(actual)}\n      expected: ${JSON.stringify(expected)}`));
  if (!ok) failures++;
}

function run(cwd, ...args) {
  // trimEnd ONLY — a full trim() eats the leading space of porcelain codes
  // like " M" and faked 2 selftest failures on the first run.
  return execFileSync(NODE, [GIT_CJS, ...args], { cwd, encoding: 'utf8' }).trimEnd();
}
function runFail(cwd, ...args) {
  try { execFileSync(NODE, [GIT_CJS, ...args], { cwd, encoding: 'utf8', stdio: 'pipe' }); return null; }
  catch (e) { return { code: e.status, stderr: e.stderr.toString() }; }
}

function realGit(cwd, ...args) {
  try { return execFileSync('git', args, { cwd, encoding: 'utf8' }).trimEnd(); }
  catch { return null; }
}

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'gitcjs-'));
const repo = path.join(tmp, 'repo');
const env = { HOME: tmp }; // isolate: selftest writes its own ~/.gitconfig
process.env.HOME = tmp;
fs.writeFileSync(path.join(tmp, '.gitconfig'), '[user]\n\tname = Self Test\n\temail = selftest@phone\n');

// --- init / rev-parse basics -------------------------------------------------
run(tmp, 'init', 'repo');
check('init creates .git', fs.existsSync(path.join(repo, '.git')), true);
check('rev-parse --is-inside-work-tree', run(repo, 'rev-parse', '--is-inside-work-tree'), 'true');
check('rev-parse --show-toplevel', run(repo, 'rev-parse', '--show-toplevel'),
      (v) => v.replace(/\//g, path.sep).toLowerCase() === repo.toLowerCase());

// --- status on untracked / add / commit ---------------------------------------
fs.writeFileSync(path.join(repo, 'a.txt'), 'alpha\n');
fs.mkdirSync(path.join(repo, 'sub'));
fs.writeFileSync(path.join(repo, 'sub', 'b.txt'), 'beta\n');
check('status: untracked', run(repo, 'status', '--porcelain'), '?? a.txt\n?? sub/b.txt');

run(repo, 'add', '-A');
check('status: staged adds', run(repo, 'status', '--porcelain'), 'A  a.txt\nA  sub/b.txt');

const commitOut = run(repo, 'commit', '-m', 'first commit');
check('commit prints [branch hash] msg', commitOut, (v) => /^\[main [0-9a-f]{7}\] first commit$/.test(v));
check('status: clean after commit', run(repo, 'status', '--porcelain'), '');
check('rev-parse --abbrev-ref HEAD', run(repo, 'rev-parse', '--abbrev-ref', 'HEAD'), 'main');
check('rev-parse --short HEAD is 7 hex', run(repo, 'rev-parse', '--short', 'HEAD'), (v) => /^[0-9a-f]{7}$/.test(v));

// --- modify / delete / porcelain codes ----------------------------------------
fs.writeFileSync(path.join(repo, 'a.txt'), 'alpha changed\n');
fs.rmSync(path.join(repo, 'sub', 'b.txt'));
check('status: modified + deleted (unstaged)', run(repo, 'status', '--porcelain'), ' M a.txt\n D sub/b.txt');

// cross-check against real git on the identical fixture, if available
const real = realGit(repo, 'status', '--porcelain');
if (real !== null) check('CROSS-CHECK vs real git status --porcelain', run(repo, 'status', '--porcelain'), real);
else console.log('SKIP  cross-check (no real git binary here)');

run(repo, 'add', 'a.txt');
run(repo, 'add', 'sub/b.txt'); // add of a deleted path stages the deletion
check('status: staged modify + staged delete', run(repo, 'status', '--porcelain'), 'M  a.txt\nD  sub/b.txt');
run(repo, 'commit', '-m', 'second: modify a, delete b');

// --- commit -am ----------------------------------------------------------------
fs.writeFileSync(path.join(repo, 'a.txt'), 'alpha third\n');
fs.writeFileSync(path.join(repo, 'untracked.txt'), 'not staged by -a\n');
run(repo, 'commit', '-am', 'third: -am stages tracked only');
check('status: -am left untracked alone', run(repo, 'status', '--porcelain'), '?? untracked.txt');

// --- log ------------------------------------------------------------------------
const logLines = run(repo, 'log', '--oneline').split('\n');
check('log --oneline has 3 commits', logLines.length, 3);
check('log --oneline newest first', logLines[0], (v) => v.endsWith('third: -am stages tracked only'));
check('log -1 limits depth', run(repo, 'log', '--oneline', '-1').split('\n').length, 1);

// --- branch / checkout -----------------------------------------------------------
run(repo, 'checkout', '-b', 'feature');
check('checkout -b switches', run(repo, 'rev-parse', '--abbrev-ref', 'HEAD'), 'feature');
fs.writeFileSync(path.join(repo, 'feat.txt'), 'feature work\n');
run(repo, 'add', 'feat.txt');
run(repo, 'commit', '-m', 'feature commit');
run(repo, 'checkout', 'main');
check('checkout main: feat.txt gone', fs.existsSync(path.join(repo, 'feat.txt')), false);
check('branch lists both, * on current', run(repo, 'branch'), '  feature\n* main');

// --- diff --name-only -------------------------------------------------------------
fs.writeFileSync(path.join(repo, 'a.txt'), 'alpha fourth\n');
// untracked.txt is still lying around — a diff must NEVER list untracked files
check('diff --name-only (untracked excluded)', run(repo, 'diff', '--name-only'), 'a.txt');
run(repo, 'add', 'a.txt');
check('diff --cached --name-only', run(repo, 'diff', '--cached', '--name-only'), 'a.txt');
check('diff --name-only clean vs index', run(repo, 'diff', '--name-only'), '');

// --- staged-then-reverted shows MM (the [1,1,3] row) --------------------------------
fs.writeFileSync(path.join(repo, 'a.txt'), 'alpha third\n'); // back to HEAD content
const mmStatus = run(repo, 'status', '--porcelain').split('\n').filter(l => l.startsWith('MM'));
check('staged-then-reverted maps to MM', mmStatus.length, 1);
run(repo, 'add', 'a.txt'); // restage actual content so later state is deterministic
run(repo, 'commit', '-m', 'settle a.txt');

// --- -C flag ----------------------------------------------------------------------
check('-C from outside the repo', run(tmp, '-C', 'repo', 'rev-parse', '--abbrev-ref', 'HEAD'), 'main');

// --- fail-loud contract -------------------------------------------------------------
const rebase = runFail(repo, 'rebase', 'main');
check('unimplemented cmd exits 2', rebase?.code, 2);
check('unimplemented cmd names itself loud', rebase?.stderr ?? '', (v) => v.includes("'rebase' not implemented on phone"));
const contentDiff = runFail(repo, 'diff');
check('content diff fails loud (exit 2)', contentDiff?.code, 2);
const noRepo = runFail(tmp, 'status');
check('outside repo exits 128', noRepo?.code, 128);

// --- summary -------------------------------------------------------------------------
console.log('');
if (failures) { console.log(`${failures} FAILURE(S)`); process.exit(1); }
console.log('ALL PASS');
fs.rmSync(tmp, { recursive: true, force: true });
