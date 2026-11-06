import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const dir = new URL('../vendor/pixel-agents/', import.meta.url);
const lock = JSON.parse(await readFile(new URL('UPSTREAM.json', dir), 'utf8'));
for (const [path, expected] of Object.entries(lock.files)) {
  const hash = createHash('sha256').update(await readFile(new URL(path, dir))).digest('hex');
  if (hash !== expected) throw new Error(`Pixel Agents source changed: ${path}`);
}
console.log(`Pixel Agents ${lock.commit}: ${Object.keys(lock.files).length} files verified`);
