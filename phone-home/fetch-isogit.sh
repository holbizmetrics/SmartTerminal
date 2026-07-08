#!/usr/bin/env bash
# Fetch the isomorphic-git UMD bundle used by bus.cjs / isogit-probe.js.
# Gitignored (like claude-js.zip) — re-run after a fresh clone. The bundle is the
# BROWSER umd build; bus.cjs shims `globalThis.self = globalThis` before requiring
# it (node has no `self`), which is enough because the git core injects fs/http and
# needs no other browser API. Pinned to a known-good version for reproducibility.
set -euo pipefail
VER="${1:-1.38.6}"
OUT="$(dirname "$0")/isogit.umd.js"
URL="https://cdn.jsdelivr.net/npm/isomorphic-git@${VER}/index.umd.min.js"
echo "fetching isomorphic-git ${VER} -> $OUT"
# node fetch (curl is walled on some machines; node's TLS is what the phone uses anyway)
node -e '
const https=require("https"),fs=require("fs");
function g(u,n){return new Promise((res,rej)=>{https.get(u,{headers:{"User-Agent":"fetch-isogit"}},r=>{
if(r.statusCode>=300&&r.statusCode<400&&r.headers.location){r.resume();return res(g(new URL(r.headers.location,u).toString(),n-1));}
if(r.statusCode!==200){r.resume();return rej(new Error("HTTP "+r.statusCode));}
const c=[];r.on("data",d=>c.push(d));r.on("end",()=>res(Buffer.concat(c)));}).on("error",rej);});}
g(process.argv[1],6).then(b=>{fs.writeFileSync(process.argv[2],b);console.log("wrote",b.length,"bytes");}).catch(e=>{console.error(e.message);process.exit(1);});
' "$URL" "$OUT"
