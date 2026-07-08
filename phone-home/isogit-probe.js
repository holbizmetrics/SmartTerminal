// Local-only proof that isomorphic-git (pure JS) runs on this node: no network,
// no credential — init a repo, commit a file, read the log back.
const path = require('path');
globalThis.self = globalThis; // UMD bundle targets the browser global `self`; node has none
const gitPath = path.join(__dirname, 'isogit.umd.js');
const git = require(gitPath);
const fs = require('fs');

const LOG = path.join(process.env.TMPDIR || '/tmp', 'isogit-probe.log');
try { fs.rmSync(LOG, { force: true }); } catch {}
const say = (m) => { console.log(m); try { fs.appendFileSync(LOG, m + '\n'); } catch {} };

(async () => {
  say('isomorphic-git version: ' + git.version());
  const dir = path.join(process.env.TMPDIR || '/tmp', 'isogit-probe');
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir, { recursive: true });

  await git.init({ fs, dir, defaultBranch: 'main' });
  fs.writeFileSync(path.join(dir, 'hello.txt'), 'JS-git ran here\n');
  await git.add({ fs, dir, filepath: 'hello.txt' });
  const sha = await git.commit({
    fs, dir,
    message: 'first commit from isomorphic-git on device',
    author: { name: 'smartterminal', email: 'phone@smartterminal' },
  });
  say('commit sha: ' + sha);

  const log = await git.log({ fs, dir });
  say('log entries: ' + log.length + ' | msg: ' + log[0].commit.message.trim());
  say('JS-GIT-OK');
})().catch((e) => { say('PROBE-ERR ' + e.message); process.exit(1); });
