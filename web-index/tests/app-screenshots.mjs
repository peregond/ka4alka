import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
const read = path => fs.readFileSync(new URL(path, import.meta.url));
const code = ts.transpileModule(read('../lib/app-screenshots.ts').toString(), { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText;
const exports = {};
vm.runInNewContext(code, { exports });
const { appScreenshot, appScreenshotsVersion } = exports;
const info = JSON.parse(read(`../public/app/${appScreenshotsVersion}/capture-info.json`));
assert.equal(info.Version, appScreenshotsVersion);
assert.equal(info.CaptureMethod, "Windows desktop client pixels");
assert.match(info.ReleaseArchiveSha256, /^[a-f0-9]{64}$/i);
assert.match(info.ReleaseDllSha256, /^[a-f0-9]{64}$/i);
const source = read('../components/product-preview.tsx').toString();
for (const language of ['ru', 'en', 'es']) {
  const dictionary = JSON.parse(read(`../lib/locales/${language}.json`));
  for (const match of source.matchAll(/(?:label|alt|caption): "([^"]+)"/g))
    assert.ok(dictionary[match[1]], `${language}: missing screenshot translation ${match[1]}`);
}
const files = [...source.matchAll(/file: "([a-z-]+\.webp)"/g)].map(match => match[1]);
assert.equal(files.length, 6);
for (const file of files) {
  assert.equal(appScreenshot('es', file), appScreenshot('en', file));
  assert.notEqual(appScreenshot('ru', file), appScreenshot('en', file));
  for (const language of ['ru', 'en']) {
    const image = read('../public' + appScreenshot(language, file));
    assert.equal(image.toString('ascii', 0, 4), 'RIFF');
    assert.equal(image.toString('ascii', 8, 12), 'WEBP');
    const capture = info.Captures.find(c => c.Language === language && c.File === `${language}/${file.replace(/\.webp$/, '.png')}`);
    assert.ok(capture, `${language}/${file} has no released-app capture evidence`);
    assert.equal(capture.PendingVisiblePosters, 0);
    assert.equal(capture.Width, 1424);
    assert.equal(capture.Height, 892);
  }
}
console.log('Released screenshot checks passed: RU/EN coverage, Spanish fallback, WebP files, provenance and loaded posters.');
