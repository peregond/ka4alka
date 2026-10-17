import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const branch = 'claude/quirky-hopper-1vlmj6';
const projectId = 'appgprj_6ac6006f8d248191acac74b581afe7ba';
const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
const output = process.argv[2];
if (!output || process.argv.length !== 3) throw new Error('Usage: node scripts/prepare-sites-source.mjs OUTPUT_DIRECTORY');
const currentRef = process.env.GITHUB_REF || `refs/heads/${git('branch', '--show-current')}`;
if (currentRef !== `refs/heads/${branch}`) throw new Error('Only the authorized Sites source branch can be packaged.');
const commit = git('rev-parse', 'HEAD');
const sourceTree = git('rev-parse', 'HEAD:web-index');
const hosting = JSON.parse(git('show', 'HEAD:web-index/.openai/hosting.json'));
if (hosting.project_id !== projectId || hosting.d1 !== 'DB' || hosting.r2 !== null) {
  throw new Error('The committed hosting configuration does not match the existing Sites project and DB binding.');
}
const files = git('ls-tree', '-r', '--name-only', 'HEAD:web-index').split('\n');
const prohibited = /(^|\/)(?:\.env(?:\..*)?|\.dev\.vars(?:\..*)?|node_modules|dist|\.next|\.wrangler|\.sites-runtime|\.git|\.aws|\.ssh)(?:\/|$)|\.(?:pem|key|pfx|p12)$/i;
const forbidden = files.filter(name => prohibited.test(name) && !/(^|\/)\.env\.(?:example|sample)$/.test(name));
if (forbidden.length) throw new Error('Refusing to package tracked credentials or generated runtime files.');
const directory = resolve(output);
mkdirSync(directory, { recursive: true });
const archiveName = `ka4alka-sites-source-${commit.slice(0, 12)}.zip`;
const archivePath = resolve(directory, archiveName);
execFileSync('git', ['archive', '--format=zip', `--output=${archivePath}`, 'HEAD:web-index'], { cwd: root, stdio: 'inherit' });
const manifest = {
  schemaVersion: 1,
  repository: 'peregond/ka4alka',
  branch,
  commit,
  sourceTree,
  projectId,
  d1: hosting.d1,
  r2: hosting.r2,
  siteUrl: 'https://ka4alka-online-new.peregon.chatgpt.site/',
  archive: archiveName,
  sourceFiles: files.map(path => ({
    path,
    sha256: createHash('sha256').update(execFileSync('git', ['show', `HEAD:web-index/${path}`], { cwd: root, maxBuffer: 64 * 1024 * 1024 })).digest('hex'),
  })),
  archiveSha256: createHash('sha256').update(readFileSync(archivePath)).digest('hex'),
};
writeFileSync(resolve(directory, 'source-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
console.log(`Prepared committed Sites source ${commit}; project ${projectId}; database binding DB.`);
