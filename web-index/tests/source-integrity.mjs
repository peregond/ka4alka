import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { verifySource } from '../scripts/verify-source.mjs';
const root = mkdtempSync(join(tmpdir(), 'kachalka-source-'));
try {
  const sources = {'lib/catalog-source.ts':'export type Media = { aliases?: string[]; imdbId?: string };',
    'lib/tvdb-title.ts':'export const parseSeries = () => ({aliases: ["Solar Opposites"]});',
    'lib/catalog-identity.ts':'export const identity = true;', 'tests/title-identity.mjs':'assert(parsed.aliases);'};
  const manifest = {repository:'peregond/ka4alka',branch:'claude/quirky-hopper-1vlmj6',projectId:'appgprj_6ac6006f8d248191acac74b581afe7ba',d1:'DB',commit:'a'.repeat(40),
    sourceFiles:Object.entries(sources).map(([path, text]) => ({path,sha256:createHash('sha256').update(text).digest('hex')}))};
  for (const [name, text] of Object.entries(sources)) {mkdirSync(dirname(join(root,name)),{recursive:true});writeFileSync(join(root,name),text);}
  assert.equal(verifySource(manifest,root),4);
  writeFileSync(join(root,'lib/tvdb-title.ts'),'export const parseSeries = () => ({});');
  writeFileSync(join(root,'lib/catalog-source.ts'),'export type Media = {};');
  assert.throws(()=>verifySource(manifest,root), error => error.message.includes('lib/tvdb-title.ts') && error.message.includes('lib/catalog-source.ts'));
  rmSync(join(root,'lib/catalog-identity.ts'));
  assert.throws(()=>verifySource(manifest,root), /Missing: lib\/catalog-identity.ts/);
  assert.throws(()=>verifySource({...manifest,sourceFiles:[...manifest.sourceFiles,{path:'../outside',sha256:'a'.repeat(64)}]},root), /Invalid source file inventory/);
  assert.throws(()=>verifySource({...manifest,sourceFiles:undefined},root), /per-file inventory/);
  console.log('PASS: stale aliases/types, missing source files and unsafe manifest paths are detected before publication');
} finally {rmSync(root,{recursive:true,force:true});}
