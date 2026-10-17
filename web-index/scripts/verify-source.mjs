import { createHash } from 'node:crypto';
import { existsSync, lstatSync, readFileSync, realpathSync } from 'node:fs';
import { dirname, isAbsolute, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

export function verifySource(manifest, root) {
  if (manifest.repository !== 'peregond/ka4alka' || manifest.branch !== 'claude/quirky-hopper-1vlmj6' ||
      manifest.projectId !== 'appgprj_6ac6006f8d248191acac74b581afe7ba' || manifest.d1 !== 'DB' ||
      !/^[a-f0-9]{40}$/.test(manifest.commit ?? '') || !Array.isArray(manifest.sourceFiles)) {
    throw new Error('Expected a current GitHub source manifest with a per-file inventory.');
  }
  const realRoot = realpathSync(root);
  const paths = new Set(); const missing = [], changed = [];
  for (const file of manifest.sourceFiles) {
    const name = file.path;
    if (typeof name !== 'string' || isAbsolute(name) || name.includes('\\') || name.split('/').some(part => !part || part === '.' || part === '..') ||
        !/^[a-f0-9]{64}$/.test(file.sha256 ?? '') || paths.has(name)) throw new Error('Invalid source file inventory.');
    paths.add(name);
    const path = resolve(root, name);
    if (!existsSync(path)) { missing.push(name); continue; }
    if (!realpathSync(path).startsWith(realRoot + sep)) throw new Error('Source path escapes its root: ' + name);
    if (!lstatSync(path).isFile()) throw new Error('Source must be a regular file: ' + name);
    if (createHash('sha256').update(readFileSync(path)).digest('hex') !== file.sha256) changed.push(name);
  }
  for (const name of ['lib/catalog-source.ts', 'lib/tvdb-title.ts', 'lib/catalog-identity.ts', 'tests/title-identity.mjs']) {
    if (!paths.has(name)) throw new Error('Incomplete source inventory: ' + name);
  }
  if (missing.length || changed.length) {
    throw new Error('Working source differs from commit ' + manifest.commit + '\nMissing: ' + (missing.join(', ') || 'none') +
      '\nChanged: ' + (changed.join(', ') || 'none') + '\nRestore these files from the exact verified archive before publishing.');
  }
  return paths.size;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length < 3 || process.argv.length > 4) throw new Error('Usage: node scripts/verify-source.mjs SOURCE_MANIFEST [SOURCE_ROOT]');
    const manifest = JSON.parse(readFileSync(process.argv[2], 'utf8'));
    const root = process.argv[3] ?? resolve(dirname(fileURLToPath(import.meta.url)), '..');
    const count = verifySource(manifest, root);
    console.log('PASS: ' + count + ' source files match GitHub commit ' + manifest.commit);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
