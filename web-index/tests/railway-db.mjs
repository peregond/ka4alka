import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
const root=mkdtempSync(join(tmpdir(),'ka4alka-sqlite-'));
const original=process.cwd();
const originalPath=process.env.DATABASE_PATH;
try {
  mkdirSync(join(root,'drizzle'));
  writeFileSync(join(root,'drizzle/0001.sql'),'CREATE TABLE entries (id INTEGER PRIMARY KEY, name TEXT UNIQUE);');
  process.env.DATABASE_PATH=join(root,'catalog.sqlite');
  process.chdir(root);
  const moduleUrl=new URL('../lib/railway-db.mjs',import.meta.url);
  const {railwayDb}=await import(moduleUrl);
  const db=railwayDb();
  await db.prepare('INSERT INTO entries(name) VALUES (?)').bind('Первый').run();
  assert.equal(await db.prepare('SELECT name FROM entries WHERE id=?').bind(1).first('name'),'Первый');
  assert.equal((await db.prepare('SELECT * FROM entries').all()).results.length,1);
  await assert.rejects(db.batch([
    db.prepare('INSERT INTO entries(name) VALUES (?)').bind('Откат'),
    db.prepare('INSERT INTO entries(name) VALUES (?)').bind('Первый'),
  ]));
  assert.equal(await db.prepare('SELECT name FROM entries WHERE name=?').bind('Откат').first(),null);
  assert.deepEqual(await db.prepare('SELECT name FROM entries').raw(),[['Первый']]);
  const reopened=spawnSync(process.execPath,['--input-type=module','-e',`import { railwayDb } from ${JSON.stringify(moduleUrl.href)}; const db=railwayDb(); console.log(await db.prepare('SELECT name FROM entries WHERE id=1').first('name'));`],{encoding:'utf8'});
  assert.equal(reopened.status,0,reopened.stderr);
  assert.equal(reopened.stdout.trim(),'Первый');
  console.log('Railway SQLite: migrations, bindings, rollback, raw rows and persistence passed');
} finally {
  process.chdir(original);
  if(originalPath===undefined)delete process.env.DATABASE_PATH;else process.env.DATABASE_PATH=originalPath;
  rmSync(root,{recursive:true,force:true});
}
