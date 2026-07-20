// Zero-dependency http transport for isomorphic-git (the official http/node
// adapter drags in simple-get + readable-stream; we only have node built-ins on
// the phone). Implements the { request } interface: takes an async-iterable body,
// returns { statusCode, headers, body } where body is an async iterable of bytes.
// Node's IncomingMessage already IS an async iterable of Buffers, so we hand it
// straight back. Follows redirects preserving method + body (git smart-http POSTs).
'use strict';
const https = require('node:https');
const http = require('node:http');

// Fail loud and fast on unreachable hosts instead of hanging for the OS-level
// TCP timeout (observed: a typo'd domain blocked git clone for minutes). Only
// covers the connect phase (DNS + TCP) — a slow-but-live transfer never trips it.
const CONNECT_TIMEOUT_MS = Number(process.env.GIT_CONNECT_TIMEOUT_MS || 8000);

async function collect(body) {
  if (!body) return undefined;
  const chunks = [];
  for await (const c of body) chunks.push(Buffer.from(c));
  return Buffer.concat(chunks);
}

async function request({ url, method = 'GET', headers = {}, body }, redirectsLeft = 5) {
  const payload = await collect(body);
  return new Promise((resolve, reject) => {
    const lib = url.startsWith('http:') ? http : https;
    const req = lib.request(url, { method, headers }, (res) => {
      const { statusCode, headers: h } = res;
      if (statusCode >= 300 && statusCode < 400 && h.location && redirectsLeft > 0) {
        res.resume(); // drain
        const next = new URL(h.location, url).toString();
        // 307/308 keep method+body; 301/302/303 to GET is the browser rule, but git
        // servers use 301/308 that preserve — keep method+body for all, safest for git.
        return resolve(request({ url: next, method, headers, body: reIter(payload) }, redirectsLeft - 1));
      }
      resolve({ url, method, statusCode, statusMessage: res.statusMessage, headers: h, body: res });
    });
    req.on('error', reject);
    req.on('socket', (socket) => {
      if (!socket.connecting) return;
      const timer = setTimeout(() => {
        req.destroy(new Error(
          `connect timeout after ${CONNECT_TIMEOUT_MS}ms — host unreachable or misspelled: ${url}`));
      }, CONNECT_TIMEOUT_MS);
      if (timer.unref) timer.unref();
      socket.once('connect', () => clearTimeout(timer));
      socket.once('close', () => clearTimeout(timer));
    });
    if (payload) req.write(payload);
    req.end();
  });
}

// wrap a Buffer back into a one-shot async iterable for redirect replays
function reIter(buf) {
  if (!buf) return undefined;
  return (async function* () { yield buf; })();
}

module.exports = { request };
