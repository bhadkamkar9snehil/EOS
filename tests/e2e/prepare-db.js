const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const target = path.join(os.tmpdir(), 'eos-e2e.db');
for (const ext of ['', '-shm', '-wal']) {
  try { fs.rmSync(target + ext, { force: true }); } catch { }
}
const source = path.join(process.env.LOCALAPPDATA || '', 'EOS_V2', 'eos.db');
if (process.env.LOCALAPPDATA && fs.existsSync(source)) {
  fs.copyFileSync(source, target);
  console.log('e2e database: copy of ' + source);
} else {
  console.log('e2e database: empty (no local EOS_V2 database to copy)');
}
