// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen: ek alan çevirilerini gerçek TV sayfasında (tv-webos/app.js + style.css), gerçek oyun karesinin üstünde çizer ve
// ekran görüntüsünü alır. Önce örnekler gerçek hattan geçirilir (windows/tests/.../DiagnosticReplayTests.cs,
// CEVIRGEN_TV_SAMPLES): "AD-olay.json" TV'ye gidecek "region" olayıdır. Bu betik her örnek için solda oyunun kendi
// kırpıntısını, sağda TV'nin aynı yerde çizdiğini yan yana koyar; birkaç örnek bir sayfada (kontrol föyü).
//
//   node scripts/tv-preview.js <örnek dizini> [çıktı dizini] [--only=a0019,b0053] [--rows=3] [--zoom=1.5]
//
// Tarayıcı: COUCHSUBS_TEST_BROWSER / CHROME_BIN ya da kurulu Chrome/Edge (başsız). Kareler telif altındadır, depoya girmez.
'use strict';
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { pathToFileURL } = require('node:url');

const tv = path.join(__dirname, '..', 'tv-webos');
const browser = [process.env.COUCHSUBS_TEST_BROWSER, process.env.CHROME_BIN,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'].find(p => p && fs.existsSync(p));

const args = process.argv.slice(2);
const flag = (name, fallback) => {
  const hit = args.find(a => a.startsWith('--' + name + '='));
  return hit ? hit.slice(name.length + 3) : fallback;
};
const positional = args.filter(a => !a.startsWith('--'));
const samples = positional[0];
const outDir = positional[1] || samples;
if (!samples || !fs.existsSync(samples)) {
  console.error('Kullanım: node scripts/tv-preview.js <örnek dizini> [çıktı dizini] [--only=ad,ad] [--rows=3] [--zoom=1.5]');
  process.exit(2);
}
if (!browser) { console.error('Chrome/Edge bulunamadı (COUCHSUBS_TEST_BROWSER).'); process.exit(2); }
const only = flag('only', '');
const rows = Math.max(1, parseInt(flag('rows', '3'), 10));
const zoom = parseFloat(flag('zoom', '1.5'));
const style = flag('style', '');   // ör. --style={"regionsAbove":true}
const WIDTH = 1920, HEIGHT = 1080;

/** TV sayfasının ekran/ek alan çizimi (tv-screen.test.js ile aynı kesitler): gerçek app.js, değiştirilmeden. */
function tvCode() {
  const app = fs.readFileSync(path.join(tv, 'app.js'), 'utf8');
  const cut = (from, until) => {
    const start = app.indexOf(from);
    const end = app.indexOf('\n  //', app.indexOf(until, start));
    if (start < 0 || end < start) throw new Error('TV işlevi bulunamadı: ' + from);
    return app.slice(start, end);
  };
  return [cut('  function validBox(box)', '  function validBox(box)'),
    cut('  var screenTimer = null;', '  function hideScreen()'),
    cut('  var regionBoxes = {};', '  function hideRegions()')].join('\n');
}

/** 1920×1080 TV sayfası: altta oyun karesi (tam kare büyütülmüş, alanın kırpıntısı kendi çözünürlüğünde), üstte TV çizimi. */
function innerPage(name, event, area) {
  const url = file => pathToFileURL(path.join(samples, file)).href;
  const frame = fs.existsSync(path.join(samples, name + '-kare.jpg')) ? `<img class="bg" src="${url(name + '-kare.jpg')}">` : '';
  const left = Math.round(area.left / 100 * WIDTH), top = Math.round(area.top / 100 * HEIGHT);
  const crop = `<img class="crop" style="left:${left}px;top:${top}px" src="${url(name + '-alan.jpg')}">`;
  return `<!doctype html><meta charset="utf-8"><style>${fs.readFileSync(path.join(tv, 'style.css'), 'utf8')}</style>
<style>html,body{margin:0;width:${WIDTH}px;height:${HEIGHT}px;overflow:hidden;background:#000}
.bg{position:absolute;left:0;top:0;width:${WIDTH}px;height:${HEIGHT}px}.crop{position:absolute}</style>
${frame}${crop}
<div id="regions" class="hidden"></div><div id="screen" class="hidden"></div><pre id="error" style="position:absolute;left:8px;top:8px;color:#f55;font:20px monospace"></pre>
<script>
function $(id) { return document.getElementById(id); }
function hideSubtitle() {}
var diag = { regions: 0, big: 0 };
try {
${tvCode()}
${style ? 'var extra = ' + style + '; if (extra.regionsAbove === true) regionsAbove = true;' : ''}
showRegion(${JSON.stringify(event)});
} catch (error) { $('error').textContent = String(error && error.stack || error); }
</script>`;
}

function shoot(html, file, width, height, temp) {
  const page = path.join(temp, path.basename(file) + '.html');
  fs.writeFileSync(page, html);
  const run = spawnSync(browser, ['--headless=new', '--disable-gpu', '--disable-background-networking', '--no-first-run',
    '--no-default-browser-check', '--hide-scrollbars', '--force-device-scale-factor=1', '--allow-file-access-from-files',
    '--window-size=' + width + ',' + height, ...(process.platform === 'linux' ? ['--no-sandbox'] : []),
    '--user-data-dir=' + path.join(temp, 'profile'), '--screenshot=' + file, pathToFileURL(page).href],
    { encoding: 'utf8', timeout: 60000, windowsHide: true });
  if (run.status !== 0 || !fs.existsSync(file)) throw new Error('Ekran görüntüsü alınamadı: ' + (run.error || run.stderr));
}

const names = fs.readdirSync(samples).filter(f => f.endsWith('-olay.json')).map(f => f.slice(0, -'-olay.json'.length))
  .filter(name => !only || only.split(',').includes(name)).sort();
if (names.length === 0) { console.error('Örnek yok (önce DiagnosticReplayTests çalışmalı).'); process.exit(1); }
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'cevirgen-tv-preview-'));
try {
  fs.mkdirSync(outDir, { recursive: true });
  const cells = [];
  for (const name of names) {
    const event = JSON.parse(fs.readFileSync(path.join(samples, name + '-olay.json'), 'utf8'));
    const info = JSON.parse(fs.readFileSync(path.join(samples, name + '-alan.json'), 'utf8'));
    const area = info.area.region;
    const inner = path.join(temp, name + '-tv.html');
    fs.writeFileSync(inner, innerPage(name, event, area));
    const x = Math.round(area.left / 100 * WIDTH), y = Math.round(area.top / 100 * HEIGHT);
    const w = Math.round((area.right - area.left) / 100 * WIDTH), h = Math.round((area.bottom - area.top) / 100 * HEIGHT);
    const texts = (event.blocks || []).map(b => b.text + (b.key ? '' : ' [blok]')).join(' · ') || (event.text || '(boş)');
    cells.push(`<div class="row"><div class="cap">${name} — ${info.lines.map(l => l.text).join(' / ')}<br><b>${texts.replace(/</g, '&lt;')}</b></div>
<div class="pair"><div class="view" style="width:${w * zoom}px;height:${h * zoom}px"><img src="${pathToFileURL(path.join(samples, name + '-alan.jpg')).href}" style="width:${w * zoom}px;height:${h * zoom}px"></div>
<div class="view" style="width:${w * zoom}px;height:${h * zoom}px"><iframe src="${pathToFileURL(inner).href}" style="width:${WIDTH}px;height:${HEIGHT}px;transform:scale(${zoom}) translate(${-x}px,${-y}px)"></iframe></div></div></div>`);
  }
  const sheets = [];
  for (let at = 0; at < cells.length; at += rows) {
    const part = cells.slice(at, at + rows);
    const html = `<!doctype html><meta charset="utf-8"><style>body{margin:0;background:#202227;color:#ddd;font:15px Segoe UI,Arial,sans-serif}
.row{padding:6px 8px 10px}.cap{height:44px;overflow:hidden;line-height:21px}.pair{display:flex;gap:8px}
.view{position:relative;overflow:hidden;background:#000;flex:none}iframe{border:0;position:absolute;left:0;top:0;transform-origin:0 0}</style>${part.join('\n')}`;
    const file = path.join(outDir, 'foy-' + String(sheets.length + 1).padStart(2, '0') + '.png');
    // Görüntü boyutu: iki görünüm yan yana; her satır başlık + görünüm.
    const first = JSON.parse(fs.readFileSync(path.join(samples, names[at] + '-alan.json'), 'utf8')).area.region;
    const w = Math.round((first.right - first.left) / 100 * WIDTH * zoom), h = Math.round((first.bottom - first.top) / 100 * HEIGHT * zoom);
    shoot(html, file, w * 2 + 24, part.length * (h + 60) + 4, temp);
    sheets.push(file);
    console.log(path.basename(file) + ': ' + names.slice(at, at + rows).join(', '));
  }
} finally {
  fs.rmSync(temp, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
