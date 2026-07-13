#!/usr/bin/env node
// git-selftest-net — transport verification for git.cjs (clone/push/pull/fetch)
// against a LOCAL smart-http server (node http + `git http-backend` CGI from the
// real git install). Hermetic: no network, no token, no GitHub. Laptop/desktop
// only (needs a real git binary); the phone never runs this — it runs git.cjs.
// Crucially also proves the push-REJECTION path fails loud (non-fast-forward),
// the bus.cjs-0a75c28 class of bug.
'use strict';
const { execFileSync, execFile, spawn } = require('node:child_process');
const http = require('node:http');
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');

const GIT_CJS = path.join(__dirname, 'git.cjs');
const NODE = process.execPath;

let gitBackendOk = true;
try { execFileSync('git', ['--version'], { stdio: 'pipe' }); }
catch { console.log('SKIP  no real git binary — transport selftest needs one (laptop-side only)'); process.exit(0); }

let failures = 0;
function check(name, actual, expected) {
  const ok = typeof expected === 'function' ? expected(actual) : actual === expected;
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}` + (ok ? '' : `\n      got:      ${JSON.stringify(actual)}\n      expected: ${JSON.stringify(expected)}`));
  if (!ok) failures++;
}
// ASYNC child calls only: the smart-http server lives in THIS process, so a
// synchronous execFileSync would block the event loop the server needs — the
// child then waits on a server that can never answer (self-deadlock; cost one
// wedged run to learn). 60s timeout so a genuine hang FAILS instead of wedging.
function runRaw(cwd, ...args) {
  return new Promise((resolve) => {
    execFile(NODE, [GIT_CJS, ...args], { cwd, encoding: 'utf8', timeout: 60000 },
      (err, stdout, stderr) => resolve({ err, stdout: (stdout ?? '').trimEnd(), stderr: (stderr ?? '').toString() }));
  });
}
async function run(cwd, ...args) {
  const r = await runRaw(cwd, ...args);
  if (r.err) throw new Error(`git.cjs ${args.join(' ')} failed: ${r.stderr || r.err.message}`);
  return r.stdout;
}
async function runFail(cwd, ...args) {
  const r = await runRaw(cwd, ...args);
  return r.err ? { code: r.err.code, stderr: r.stderr } : null;
}
function realGit(cwd, ...args) { return execFileSync('git', args, { cwd, encoding: 'utf8' }).trimEnd(); }

// --- minimal smart-http server: node http -> git http-backend (CGI) -----------
function serve(projectRoot) {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      // Buffer the whole body first: git http-backend is CGI and reads exactly
      // CONTENT_LENGTH bytes — piping without it made the backend block forever
      // (first run of this selftest hung on the clone).
      const chunks = [];
      req.on('data', (d) => chunks.push(d));
      req.on('end', () => {
        const body = Buffer.concat(chunks);
        const u = new URL(req.url, 'http://localhost');
        const env = {
          ...process.env,
          GIT_PROJECT_ROOT: projectRoot,
          GIT_HTTP_EXPORT_ALL: '1',
          PATH_INFO: decodeURIComponent(u.pathname),
          QUERY_STRING: u.searchParams.toString(),
          REQUEST_METHOD: req.method,
          CONTENT_TYPE: req.headers['content-type'] || '',
          CONTENT_LENGTH: String(body.length),
          GATEWAY_INTERFACE: 'CGI/1.1',
          SERVER_PROTOCOL: 'HTTP/1.1',
          REMOTE_ADDR: '127.0.0.1',
          GIT_HTTP_RECEIVE_PACK: '1', // allow anonymous push — it's a loopback test server
        };
        handle(body, env, res);
      });
    });
    function handle(body, env, res) {
      const cgi = spawn('git', ['http-backend'], { env });
      cgi.stderr.on('data', (d) => process.stderr.write('[http-backend] ' + d));
      if (body.length) cgi.stdin.write(body);
      cgi.stdin.end();
      let buf = Buffer.alloc(0), headersDone = false;
      cgi.stdout.on('data', (d) => {
        if (headersDone) return res.write(d);
        buf = Buffer.concat([buf, d]);
        const sep = buf.indexOf('\r\n\r\n');
        if (sep === -1) return;
        headersDone = true;
        for (const line of buf.slice(0, sep).toString().split('\r\n')) {
          const i = line.indexOf(':');
          if (i > 0) {
            const k = line.slice(0, i).trim(), v = line.slice(i + 1).trim();
            if (k.toLowerCase() === 'status') res.statusCode = parseInt(v, 10);
            else res.setHeader(k, v);
          }
        }
        res.write(buf.slice(sep + 4));
      });
      cgi.stdout.on('end', () => res.end());
      cgi.on('error', () => { res.statusCode = 500; res.end(); });
    }
    server.listen(0, '127.0.0.1', () => resolve(server));
  });
}

(async () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'gitcjs-net-'));
  process.env.HOME = tmp;
  fs.writeFileSync(path.join(tmp, '.gitconfig'), '[user]\n\tname = Net Test\n\temail = net@phone\n');

  // upstream bare repo with one seed commit
  const bare = path.join(tmp, 'upstream.git');
  realGit(tmp, 'init', '--bare', '-b', 'main', bare);
  realGit(bare, 'config', 'http.receivepack', 'true'); // anonymous push on the loopback test server
  const seed = path.join(tmp, 'seed');
  realGit(tmp, 'init', '-b', 'main', seed);
  fs.writeFileSync(path.join(seed, 'README.md'), 'seed\n');
  realGit(seed, 'add', '.');
  realGit(seed, '-c', 'user.name=Seeder', '-c', 'user.email=s@s', 'commit', '-m', 'seed commit');
  realGit(seed, 'push', bare.replace(/\\/g, '/'), 'main');

  const server = await serve(tmp);
  const url = `http://127.0.0.1:${server.address().port}/upstream.git`;

  try {
    // --- clone -----------------------------------------------------------------
    const workA = path.join(tmp, 'workA');
    check('clone over smart-http', await run(tmp, 'clone', url, workA), (v) => v.includes('Cloned'));
    check('clone got the seed commit', await run(workA, 'log', '--oneline'), (v) => v.endsWith('seed commit'));

    // --- commit + push, verified on the SERVER side ------------------------------
    fs.writeFileSync(path.join(workA, 'from-phone-cli.txt'), 'hello from git.cjs\n');
    await run(workA, 'add', '-A');
    await run(workA, 'commit', '-m', 'pushed via git.cjs');
    check('push reports ok', await run(workA, 'push'), (v) => v.startsWith('push ok'));
    check('SERVER actually has the pushed commit', realGit(bare, 'log', '--oneline', '-1'), (v) => v.endsWith('pushed via git.cjs'));

    // --- pull picks up upstream changes -------------------------------------------
    const workB = path.join(tmp, 'workB');
    await run(tmp, 'clone', url, workB);
    fs.writeFileSync(path.join(workB, 'second.txt'), 'upstream moved\n');
    await run(workB, 'add', '-A');
    await run(workB, 'commit', '-m', 'upstream moved');
    await run(workB, 'push');
    check('pull fast-forwards', await run(workA, 'pull'), (v) => v.startsWith('pull ok'));
    check('pulled file arrived', fs.existsSync(path.join(workA, 'second.txt')), true);
    check('fetch runs clean', await run(workA, 'fetch'), (v) => v.startsWith('fetch ok'));

    // --- push REJECTION must fail loud (the 0a75c28 contract) ----------------------
    // workB diverges from workA: both commit; workB pushes first; workA's push is non-ff.
    fs.writeFileSync(path.join(workB, 'race.txt'), 'B wins\n');
    await run(workB, 'add', '-A'); await run(workB, 'commit', '-m', 'B race'); await run(workB, 'push');
    fs.writeFileSync(path.join(workA, 'race.txt'), 'A loses\n');
    await run(workA, 'add', '-A'); await run(workA, 'commit', '-m', 'A race');
    const rejected = await runFail(workA, 'push');
    check('non-ff push exits nonzero', rejected !== null, true);
    check('non-ff push says REJECTED loud', rejected?.stderr ?? '', (v) => /REJECTED|not a simple fast-forward|failed/i.test(v));
    check('server kept B (A did not sneak in)', realGit(bare, 'log', '--oneline', '-1'), (v) => v.endsWith('B race'));
  } finally {
    server.close();
  }

  console.log('');
  if (failures) { console.log(`${failures} FAILURE(S)`); process.exit(1); }
  console.log('ALL PASS');
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch {} // git http-backend may hold a handle briefly on Windows
})();
