// Tek dosyalık prototipi üretir: docs/design/prototype/release/BuildOrchestrator-prototype-<sürüm>.html (git-ignore'lu).
// Kaynaklar repodadır: app/ (prototip), Main.dc.html (sahne listesi), ../../design-system (token'lar, bileşenler, logolar),
// uygulamanın CHANGELOG.md'si (What's new). Çalıştırma: bu klasörde `npm install`, sonra `npm run build`. Sürüm
// app/BuildApp.jsx'teki DESIGN_VERSION'dır.
import { build, transform } from 'esbuild';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const ds = join(here, '..', '..', 'design-system');
const read = (...p) => readFileSync(join(...p), 'utf8');
const svg = (...p) => 'data:image/svg+xml;base64,' + readFileSync(join(...p)).toString('base64');

const app = read(here, 'app', 'BuildApp.jsx');
const version = (app.match(/const DESIGN_VERSION = '([^']+)'/) || [])[1];
if (!version) throw new Error('DESIGN_VERSION not found in app/BuildApp.jsx');

// What's new verisi tek yerde: uygulamanın CHANGELOG.md'si. app/release-notes.js ondan üretilir ve her build'de yenilenir;
// tuval (Main.dc.html) de aynı dosyayı yükler, o yüzden repoda durur ve elle değiştirilmez. Okuma kuralları uygulamanın
// ayrıştırıcısınınkiyle aynıdır (src/BuildOrchestrator.App/Services/ReleaseNotes.cs, Parse): girintili satır maddenin devamıdır,
// başka her satır satır numarasıyla reddedilir.
const KINDS = { Added: 'feature', Changed: 'change', Fixed: 'fix', Performance: 'perf', Removed: 'removed' };
function releaseNotes(changelog) {
  const versions = [];
  let kind = null;
  let continuable = false; // son satır bir madde (ya da devamı) mı
  changelog.split('\n').forEach((raw, i) => {
    const line = raw.replace(/[\r \t]+$/, '');
    const where = `CHANGELOG.md:${i + 1}`;
    if (line === '') { continuable = false; return; }
    if (line.startsWith('## ')) {
      const head = line.match(/^## \[(\d+\.\d+\.\d+)\] - (\d{4}-\d{2}-\d{2})$/);
      if (!head) throw new Error(`${where}: a version heading reads '## [major.minor.patch] - yyyy-MM-dd'`);
      versions.push({ v: head[1], date: head[2], items: [] });
      kind = null;
      continuable = false;
      return;
    }
    if (versions.length === 0) return; // başlık ve giriş metni
    if (line.startsWith('### ')) {
      const name = line.slice(4).trim();
      kind = KINDS[name];
      if (!kind) throw new Error(`${where}: unknown category '${name}'`);
      continuable = false;
      return;
    }
    const items = versions.at(-1).items;
    if (line.startsWith('- ')) {
      if (!kind) throw new Error(`${where}: a note must sit under a category heading`);
      items.push([kind, line.slice(2).trim()]);
      continuable = true;
      return;
    }
    if (continuable && /^\s/.test(line)) { items.at(-1)[1] += ' ' + line.trim(); return; }
    throw new Error(`${where}: unexpected text; only headings and '- ' notes are allowed inside a version`);
  });
  if (versions.length === 0) throw new Error('no version sections in CHANGELOG.md');
  for (const { v, items } of versions) if (items.length === 0) throw new Error(`CHANGELOG.md: version ${v} has no notes`);
  const kinds = Object.entries(KINDS).map(([from, to]) => `${from}→${to}`).join(' · ');
  return "/* Build Orchestrator — sürüm notları (What's new; uygulama sürümleri). build.mjs uygulamanın CHANGELOG.md'sinden\n"
    + `   üretir, elle değiştirilmez: ${kinds}. En yeni sürüm en üstte. */\n`
    + 'window.DELTA_BO_NOTES = [\n'
    + versions.map(({ v, date, items }) => `  {\n    v: ${JSON.stringify(v)}, date: ${JSON.stringify(date)}, items: [\n`
      + items.map(([k, text]) => `      [${JSON.stringify(k)}, ${JSON.stringify(text)}],\n`).join('')
      + '    ],\n  },\n').join('')
    + '];\n';
}
const notesPath = join(here, 'app', 'release-notes.js');
const notes = releaseNotes(read(here, '..', '..', '..', 'CHANGELOG.md'));
if (!existsSync(notesPath) || read(notesPath) !== notes) {
  writeFileSync(notesPath, notes);
  console.log('app/release-notes.js updated from CHANGELOG.md');
}

// Sahne listesi tek yerde: Main.dc.html'in scenes() listesi (tuvaldeki şerit de onu gösterir).
const scenes = [...read(here, 'Main.dc.html').matchAll(/\{ id: (\d+), label: '([^']*)', desc: '([^']*)' \}/g)]
  .map(([, id, label, desc]) => ({ id: Number(id), label, desc }));
if (scenes.length === 0) throw new Error('no scenes found in Main.dc.html');

// Logolar artifact'te yüklemedir (/_blob/…); tek dosyada design system'deki SVG'leri gömülür.
const withLogos = app
  .replace(/(const LOGO = \{ mark: )'[^']*'/, `$1'${svg(ds, 'assets', 'Logos', 'app-mark.svg')}'`)
  .replace(/(, company: )'[^']*'( \};)/, `$1'${svg(ds, 'assets', 'Logos', 'delta-logo-dark.svg')}'$2`);
if (withLogos === app || /\/_blob\//.test(withLogos.match(/const LOGO = [^\n]*/)[0])) throw new Error('LOGO line not rewritten');

const react = await build({
  stdin: {
    contents: "import * as React from 'react'; import * as ReactDOM from 'react-dom'; import * as ReactDOMClient from 'react-dom/client';"
      + ' window.React = React; window.ReactDOM = { ...ReactDOM, ...ReactDOMClient };',
    resolveDir: here,
  },
  bundle: true, minify: true, format: 'iife', write: false, legalComments: 'none',
  define: { 'process.env.NODE_ENV': '"production"' },
});
const compiled = await transform(withLogos, { loader: 'jsx' });

// Gömülen betiklerde </script dizisi kaçırılır; JS anlamı değişmez.
const inline = (js) => js.replace(/<\/script/gi, '<\\/script');
const parts = {
  VERSION: version,
  TOKENS_CSS: read(ds, 'tokens.css'),
  BUNDLE_CSS: read(ds, 'components', 'bundle.css'),
  REACT: inline(react.outputFiles[0].text),
  DS_BUNDLE: inline(read(ds, 'components', 'bundle.js')),
  BUILD_DATA: inline(read(here, 'app', 'build-data.js')),
  RELEASE_NOTES: inline(notes),
  BUILD_APP: inline(compiled.code),
  APP_ICON: svg(ds, 'assets', 'App Icons', 'app-icon.svg'),
  SCENES: JSON.stringify(scenes),
};
const html = read(here, 'standalone.html').replace(/\{\{(\w+)\}\}/g, (m, k) => {
  if (!(k in parts)) throw new Error('unknown placeholder ' + m);
  return parts[k];
});

const outDir = join(here, 'release');
mkdirSync(outDir, { recursive: true });
const out = join(outDir, `BuildOrchestrator-prototype-${version}.html`);
writeFileSync(out, html);
console.log(`${out} (${Math.round(html.length / 1024)} KB)`);
