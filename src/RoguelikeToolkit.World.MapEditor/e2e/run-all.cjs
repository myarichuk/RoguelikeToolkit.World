'use strict';
// Runs every check in this folder in its own process (each owns a browser) and summarises. Exit code 1 if any fail.
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const only = process.argv.slice(2);
const files = fs.readdirSync(__dirname).filter((f) => f.endsWith('.cjs') && !['harness.cjs', 'run-all.cjs'].includes(f)).sort()
  .filter((f) => only.length === 0 || only.some((o) => f.includes(o)));
const results = [];
for (const f of files) {
  const r = spawnSync(process.execPath, [path.join(__dirname, f)], { stdio: 'inherit' });
  results.push([f, r.status === 0]);
}
console.log('\n' + results.map(([f, ok]) => `${ok ? 'PASS' : 'FAIL'}  ${f}`).join('\n'));
process.exit(results.every(([, ok]) => ok) && results.length > 0 ? 0 : 1);
