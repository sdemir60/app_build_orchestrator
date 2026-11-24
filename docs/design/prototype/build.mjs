// Tek dosyalık prototipi üretir: docs/design/prototype/release/BuildOrchestrator-prototype-<sürüm>.html (git-ignore'lu).
// Kaynaklar repodadır: app/ (prototip), Main.dc.html (sahne listesi), ../../design-system (token'lar, bileşenler, logolar).
// Çalıştırma: bu klasörde `npm install`, sonra `npm run build`. Sürüm app/BuildApp.jsx'teki DESIGN_VERSION'dır.
import { build, transform } from 'esbuild';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const ds = join(here, '..', '..', 'design-system');
const read = (...p) => readFileSync(join(...p), 'utf8');
const svg = (...p) => 'data:image/svg+xml;base64,' + readFileSync(join(...p)).toString('base64');

const app = read(here, 'app', 'BuildApp.jsx');
const version = (app.match(/const DESIGN_VERSION = '([^']+)'/) || [])[1];
if (!version) throw new Error('DESIGN_VERSION not found in app/BuildApp.jsx');

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
  RELEASE_NOTES: inline(read(here, 'app', 'release-notes.js')),
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
