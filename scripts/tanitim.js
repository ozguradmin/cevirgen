// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen: tanıtım görsellerini üretir (depo ve sosyal medya). Her şey özgündür: sahne Çevirgen'in kendi gece şehri
// çizimi (design/cevirgen/sahne-3840.png; uygulamadaki PerdeBrushes.Scene'den, "Cevirgen.exe --render-screens" ile),
// yazı tipleri uygulamanın gömülü OFL yazı tipleri, örnek replik bu iş için yazılmış bir cümle. Oyun görüntüsü, oyun
// repliği ya da logo yoktur.
//
//   node scripts/tanitim.js [çıktı dizini]        (varsayılan: docs/cevirgen/tanitim)
//
// Tarayıcı: COUCHSUBS_TEST_BROWSER / CHROME_BIN ya da kurulu Chrome/Edge (başsız). Çıktılar 2× çözünürlükte PNG'dir:
//   kapak.png, sinema.png, klasik-kutu.png, iki-gorunum.png, nasil-calisir.png (3200×1800) ve
//   github-onizleme.png (2560×1280; deponun "Social preview" görseli).
'use strict';
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { pathToFileURL } = require('node:url');

const repo = path.join(__dirname, '..');
const outDir = path.resolve(process.argv[2] || path.join(repo, 'docs', 'cevirgen', 'tanitim'));
const browser = [process.env.COUCHSUBS_TEST_BROWSER, process.env.CHROME_BIN,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'].find(p => p && fs.existsSync(p));
if (!browser) { console.error('Chrome/Edge bulunamadı (COUCHSUBS_TEST_BROWSER).'); process.exit(2); }

const url = (...parts) => pathToFileURL(path.join(repo, ...parts)).href;
const font = name => url('windows', 'src', 'Cevirgen.App', 'Assets', 'Fonts', name);
const SCENE = url('design', 'cevirgen', 'sahne-3840.png');
const REPO = 'github.com/ozguradmin/cevirgen';
const TAGLINE = 'Oyunlar için canlı Türkçe altyazı';

// Örnek replik: bu görseller için yazıldı.
const EN = 'We leave at dawn. Don’t be late.';
const TR = 'Şafakta yola çıkıyoruz. Geç kalma.';

/** Yazının çevresine bulanıksız kenarlık (TV'deki "Sinema" görünümü gibi): kaydırılmış gölgeler. */
function outline(width, color) {
  const shadows = [];
  for (let step = 0; step < 24; step++) {
    const angle = step / 24 * Math.PI * 2;
    shadows.push(`${(Math.cos(angle) * width).toFixed(2)}px ${(Math.sin(angle) * width).toFixed(2)}px 0 ${color}`);
  }
  return shadows.join(', ');
}

// Bütün yerleşim gövdeye göredir (position: relative): başsız tarayıcının görüntü alanı ekran görüntüsünden önce başka
// boyda olabilir; gövde sabit boydadır.
const BASE = `
@font-face { font-family: "Plex"; font-weight: 400; src: url("${font('IBMPlexSans-Regular.ttf')}"); }
@font-face { font-family: "Plex"; font-weight: 500; src: url("${font('IBMPlexSans-Medium.ttf')}"); }
@font-face { font-family: "Plex"; font-weight: 600; src: url("${font('IBMPlexSans-SemiBold.ttf')}"); }
@font-face { font-family: "Plex"; font-weight: 700; src: url("${font('IBMPlexSans-Bold.ttf')}"); }
@font-face { font-family: "PlexMono"; font-weight: 500; src: url("${font('IBMPlexMono-Medium.ttf')}"); }
@font-face { font-family: "Archivo"; font-weight: 700; src: url("${font('ArchivoCondensed-Bold.ttf')}"); }
@font-face { font-family: "Archivo"; font-weight: 800; src: url("${font('ArchivoCondensed-ExtraBold.ttf')}"); }
:root { --graphite: #0F1014; --panel: #14161C; --line: #262A33; --text: #F3F4F6; --soft: #C9CDD6; --muted: #9AA0AC; --yellow: #FFD23F; }
* { box-sizing: border-box; }
html, body { margin: 0; }
body { position: relative; overflow: hidden; background: var(--graphite); color: var(--text); font-family: "Plex", sans-serif; }
.mark { font-family: "Archivo", sans-serif; font-weight: 800; letter-spacing: -0.01em; line-height: 1; }
.mark b { color: var(--yellow); font-weight: 800; }
.mono { font-family: "PlexMono", monospace; font-weight: 500; letter-spacing: 0.06em; text-transform: uppercase; }
.url { font-family: "PlexMono", monospace; font-weight: 500; letter-spacing: 0.02em; color: var(--muted); }
.scene { position: absolute; inset: 0; background: linear-gradient(180deg, rgba(78, 96, 138, 0) 62%, rgba(78, 96, 138, 0.34) 68%, rgba(78, 96, 138, 0.3) 84%, rgba(78, 96, 138, 0.16) 100%), url("${SCENE}") center / cover no-repeat; }
.lockup { display: flex; align-items: center; gap: 22px; }
.lockup i { display: block; width: 1px; height: 30px; background: rgba(255, 255, 255, 0.24); }
.lockup .mono { font-size: 16px; color: #B9BEC8; }
.subs { position: absolute; left: 0; right: 0; display: flex; flex-direction: column; }
.row { display: flex; align-items: center; }
.side { flex: 1 1 0; min-width: 0; }
.lead { display: flex; align-items: center; padding-left: 64px; color: var(--yellow); font-size: 17px; white-space: nowrap; }
.lead.dim { color: #C4C8D0; }
.lead i { position: relative; display: block; flex: 1; height: 1px; margin: 0 28px 0 16px; background: currentColor; opacity: 0.75; }
.lead i::after { content: ""; position: absolute; right: -3px; top: -3px; width: 7px; height: 7px; border-radius: 50%; background: currentColor; }
.tr { font-weight: 700; color: #fff; white-space: nowrap; line-height: 1.2; }
.en { font-weight: 500; color: rgba(255, 255, 255, 0.92); white-space: nowrap; line-height: 1.2; text-shadow: 0 2px 3px rgba(0, 0, 0, 0.9), 0 0 2px rgba(0, 0, 0, 0.9); }
.box { background: #101014; font-weight: 700; color: #fff; white-space: nowrap; line-height: 1.3; }
.tv { position: relative; overflow: hidden; background: #000; }
.foot { position: absolute; left: 64px; right: 64px; bottom: 44px; display: flex; align-items: center; justify-content: space-between; font-size: 18px; }
.foot .mono { font-size: 16px; color: var(--muted); }
`;

const page = (width, height, css, body) => `<!doctype html><html lang="tr"><meta charset="utf-8"><style>${BASE}
body { width: ${width}px; height: ${height}px; }
${css}</style>${body}</html>`;

const lockup = size => `<div class="lockup"><div class="mark" style="font-size:${size}px"><b>ç</b>evirgen</div><i></i><span class="mono">${TAGLINE}</span></div>`;

/**
 * TV'deki altyazı. "sinema": kenarlıklı Türkçe, oyunun İngilizcesinin hemen üstünde. "klasik": koyu kutu İngilizcenin
 * yerinde. "oyun": yalnız oyunun İngilizcesi (çeviri gelmeden önce). notes: satırların solunda açıklama ve kılavuz çizgisi.
 */
function subtitles(look, size, bottom, notes) {
  const round = Math.round;
  const lead = (text, dim) => notes ? `<div class="side lead${dim ? ' dim' : ''}"><span class="mono">${text}</span><i></i></div>` : '<div class="side"></div>';
  const row = (left, inner) => `<div class="row">${left}${inner}<div class="side"></div></div>`;
  const rows = [];
  if (look === 'klasik') {
    rows.push(row(lead('Türkçe · Çevirgen'),
      `<div class="box" style="font-size:${size}px;padding:${round(size * 0.2)}px ${round(size * 0.5)}px;border-radius:${round(size * 0.15)}px">${TR}</div>`));
  } else {
    if (look === 'sinema') {
      rows.push(row(lead('Türkçe · Çevirgen'),
        `<div class="tr" style="font-size:${size}px;text-shadow:${outline(Math.max(1.5, size / 15), '#000')}">${TR}</div>`));
    }
    rows.push(row(lead('Oyunun altyazısı', true), `<div class="en" style="font-size:${round(size * 0.72)}px">${EN}</div>`));
  }
  return `<div class="subs" style="bottom:${bottom}px;gap:${round(size * 0.2)}px">${rows.join('')}</div>`;
}

/** Çerçeveli TV ekranı: sahne ve altyazı. */
function tv(width, look, size) {
  const height = Math.round(width * 9 / 16);
  return `<div class="tv" style="width:${width}px;height:${height}px"><div class="scene"></div>${subtitles(look, size, Math.round(height * 0.12))}</div>`;
}

/** Tam sahne kartı (TV'de böyle görünür): üstte ad, altta altyazı ve görünümün adı; solda hangi satırın ne olduğu. */
function lookCard(look) {
  const name = look === 'klasik' ? 'Klasik kutu' : 'Sinema';
  const detail = look === 'klasik' ? 'koyu kutu · İngilizcenin yerine geçer' : 'zeminsiz · kalın · siyah kenarlık';
  return page(1600, 900, `
.shade { position: absolute; inset: 0; background: linear-gradient(180deg, rgba(8, 10, 14, 0.5) 0, rgba(8, 10, 14, 0) 22%, rgba(8, 10, 14, 0) 72%, rgba(8, 10, 14, 0.4) 100%); }
.lockup { position: absolute; left: 64px; top: 50px; }
.pill { display: flex; align-items: baseline; gap: 14px; padding: 13px 28px 14px 22px; border: 1px solid rgba(255, 255, 255, 0.16); border-radius: 999px; background: rgba(15, 16, 20, 0.8); }
.pill .dot { width: 11px; height: 11px; border-radius: 50%; background: var(--yellow); align-self: center; }
.pill .mono { font-size: 15px; color: var(--yellow); }
.pill .name { font-size: 28px; font-weight: 600; }
.pill .detail { font-size: 21px; color: var(--muted); }
`, `<div class="scene"></div><div class="shade"></div>
${lockup(54)}
${subtitles(look, 52, 178, true)}
<div class="foot"><div class="pill"><span class="dot"></span><span class="mono">Görünüm</span><span class="name">${name}</span><span class="detail">${detail}</span></div>
  <span class="url">${REPO}</span></div>`);
}

/** Kapak: solda ad, söz ve üç bilgi; sağda TV'de Sinema görünümü. */
function cover(width, height) {
  const k = width / 1600;
  const px = value => Math.round(value * k) + 'px';
  const screen = Math.round(width * 0.535);
  return page(width, height, `
.left { position: absolute; left: ${px(72)}; top: 0; bottom: 0; width: ${Math.round(width * 0.36)}px; display: flex; flex-direction: column; justify-content: center; }
.mark { font-size: ${px(92)}; margin-bottom: ${px(34)}; }
h1 { margin: 0 0 ${px(22)}; font-family: "Archivo", sans-serif; font-weight: 700; font-size: ${px(62)}; line-height: 1.04; letter-spacing: -0.005em; }
p { margin: 0 0 ${px(34)}; font-size: ${px(25)}; line-height: 1.42; color: var(--soft); }
ul { list-style: none; margin: 0 0 ${px(38)}; padding: 0; font-size: ${px(23)}; font-weight: 500; }
li { display: flex; align-items: center; gap: ${px(14)}; padding: ${px(11)} 0; border-top: 1px solid var(--line); }
li:last-child { border-bottom: 1px solid var(--line); }
li::before { content: ""; width: ${px(9)}; height: ${px(9)}; border-radius: 50%; background: var(--yellow); flex: none; }
.url { font-size: ${px(18)}; }
.right { position: absolute; right: ${px(70)}; top: 0; bottom: 0; display: flex; align-items: center; }
.tv { border-radius: ${px(10)}; box-shadow: 0 0 0 ${px(7)} #1A1D24, 0 0 0 ${px(8)} #2E333F, 0 ${px(34)} ${px(90)} rgba(0, 0, 0, 0.6); }
`, `<div class="left">
  <div class="mark"><b>ç</b>evirgen</div>
  <h1>Oyunlar için canlı<br>Türkçe altyazı</h1>
  <p>Ekrandaki İngilizce altyazıyı okur, Türkçeye çevirir ve TV’de oyunun üstünde gösterir.</p>
  <ul><li>PS5 · Xbox · PC</li><li>İnternetsiz, ücretsiz çeviri</li><li>Açık kaynak · AGPL-3.0</li></ul>
  <div class="url">${REPO}</div>
</div>
<div class="right">${tv(screen, 'sinema', Math.round(34 * k))}</div>`);
}

const INFO = `
.lockup { position: absolute; left: 64px; top: 48px; }
.title { position: absolute; left: 64px; right: 64px; top: 142px; display: flex; align-items: baseline; gap: 28px; }
.title h1 { margin: 0; font-family: "Archivo", sans-serif; font-weight: 700; font-size: 66px; line-height: 1; letter-spacing: -0.005em; white-space: nowrap; }
.title p { margin: 0; font-size: 24px; color: var(--muted); }
.tv { border-radius: 10px; box-shadow: 0 0 0 1px #2E333F, 0 22px 60px rgba(0, 0, 0, 0.5); }
`;

/** İki görünüm yan yana: Sinema ve Klasik kutu. */
function twoLooks() {
  const look = (id, name, detail) => `<figure>${tv(716, id, 30)}
    <figcaption><div class="name">${name}</div><div class="detail">${detail}</div></figcaption></figure>`;
  return page(1600, 900, `${INFO}
.pair { position: absolute; left: 64px; right: 64px; top: 252px; display: flex; gap: 40px; }
figure { margin: 0; width: 716px; }
figcaption { margin-top: 30px; }
.name { display: flex; align-items: center; gap: 14px; font-size: 32px; font-weight: 600; line-height: 1.2; }
.name::before { content: ""; width: 11px; height: 11px; border-radius: 50%; background: var(--yellow); flex: none; }
.detail { margin-top: 8px; padding-left: 25px; font-size: 22px; line-height: 1.4; color: var(--soft); }
`, `${lockup(48)}
<div class="title"><h1>İki görünüm</h1><p>Altyazının TV’de nasıl duracağını sen seçersin.</p></div>
<div class="pair">
  ${look('sinema', 'Sinema', 'Zeminsiz, kalın, siyah kenarlıklı. İngilizcenin hemen üstünde.')}
  ${look('klasik', 'Klasik kutu', 'Koyu kutu. İngilizcenin yerine geçer.')}
</div>
<div class="foot"><span class="mono">Görünüm sekmesinden tek tıkla değişir</span><span class="url">${REPO}</span></div>`);
}

/** Nasıl çalışır: oyun → Çevirgen okur ve çevirir → TV'de gösterir. */
function howItWorks() {
  const width = 453;
  const arrow = `<div class="arrow"><svg width="40" height="16" viewBox="0 0 40 16" fill="none" stroke="#FFD23F" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 8h36M30 1.5 37.5 8 30 14.5"/></svg></div>`;
  const step = (number, label, title, text, thumb) => `<div class="step">${thumb}
    <div class="mono">${number} · ${label}</div><h2>${title}</h2><p>${text}</p></div>`;
  const panel = `<div class="tv panel" style="width:${width}px;height:${Math.round(width * 9 / 16)}px">
    <div class="line"><span class="mono">Okunan · İngilizce</span><div class="read">${EN}</div></div>
    <div class="split"><i></i><svg width="14" height="18" viewBox="0 0 14 18" fill="none" stroke="#FFD23F" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 1v15M1.5 10.5 7 16.5l5.5-6"/></svg><i></i></div>
    <div class="line"><span class="mono yellow">Çeviri · Türkçe</span><div class="said">${TR}</div></div>
  </div>`;
  return page(1600, 900, `${INFO}
.steps { position: absolute; left: 64px; right: 64px; top: 252px; display: flex; }
.step { width: ${width}px; flex: none; }
.step > .mono { margin-top: 30px; font-size: 15px; color: var(--yellow); }
.step h2 { margin: 10px 0 10px; font-size: 30px; font-weight: 600; line-height: 1.2; }
.step p { margin: 0; font-size: 21px; line-height: 1.48; color: var(--soft); text-wrap: pretty; }
.needs { position: absolute; left: 64px; right: 64px; top: 738px; display: flex; align-items: center; gap: 20px; padding: 17px 26px 18px; border: 1px solid var(--line); border-radius: 12px; font-size: 20px; color: var(--soft); }
.needs .mono { margin-right: 12px; font-size: 14px; color: var(--yellow); }
.needs i { display: block; width: 5px; height: 5px; border-radius: 50%; background: #5B616E; }
.arrow { flex: 1; height: ${Math.round(width * 9 / 16)}px; display: flex; align-items: center; justify-content: center; }
.panel { display: flex; flex-direction: column; justify-content: center; gap: 20px; padding: 0 30px; background: var(--panel); }
.panel .mono { font-size: 13px; color: var(--muted); }
.panel .mono.yellow { color: var(--yellow); }
.read { margin-top: 8px; font-size: 20px; font-weight: 500; color: var(--soft); white-space: nowrap; }
.said { margin-top: 8px; font-size: 23px; font-weight: 700; color: #fff; white-space: nowrap; }
.split { display: flex; align-items: center; gap: 14px; }
.split i { display: block; flex: 1; height: 1px; background: var(--line); }
`, `${lockup(48)}
<div class="title"><h1>Nasıl çalışır</h1><p>Konsola ya da oyuna hiçbir şey kurulmaz.</p></div>
<div class="steps">
  ${step('01', 'Oyun', 'Sen oynarsın', 'PS5, Xbox ya da bilgisayar. Çevirgen oyunun görüntüsünü izler; oyuna dokunmaz.', tv(width, 'oyun', 21))}
  ${arrow}
  ${step('02', 'Çevirgen', 'Okur ve çevirir', 'İngilizce altyazıyı ekrandan okur, Türkçeye çevirir. Varsayılan çeviri bilgisayarında, internetsiz çalışır.', panel)}
  ${arrow}
  ${step('03', 'TV', 'Oyunun üstünde gösterir', 'Türkçe satır, Android TV’de İngilizcenin hemen üstünde belirir. Oyundaki altyazı gidince o da gider.', tv(width, 'sinema', 21))}
</div>
<div class="needs"><span class="mono">Gerekenler</span><span>Windows bilgisayar</span><i></i><span>Android TV ya da bilgisayar ekranı</span><i></i><span>PS5’te ücretsiz ikinci bir PSN hesabı</span></div>
<div class="foot"><span class="mono">Açık kaynak · AGPL-3.0</span><span class="url">${REPO}</span></div>`);
}

function shoot(html, file, width, height, temp) {
  const source = path.join(temp, path.basename(file) + '.html');
  fs.writeFileSync(source, html);
  const run = spawnSync(browser, ['--headless=new', '--disable-gpu', '--disable-background-networking', '--no-first-run',
    '--no-default-browser-check', '--hide-scrollbars', '--force-device-scale-factor=2', '--allow-file-access-from-files',
    '--virtual-time-budget=4000', '--window-size=' + width + ',' + height, ...(process.platform === 'linux' ? ['--no-sandbox'] : []),
    '--user-data-dir=' + path.join(temp, 'profile'), '--screenshot=' + file, pathToFileURL(source).href],
    { encoding: 'utf8', timeout: 90000, windowsHide: true });
  if (run.status !== 0 || !fs.existsSync(file)) throw new Error('Görsel alınamadı: ' + path.basename(file) + ' ' + (run.error || run.stderr));
  console.log(path.basename(file) + ' ' + (fs.statSync(file).size / 1024).toFixed(0) + ' KB');
}

const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'cevirgen-tanitim-'));
try {
  fs.mkdirSync(outDir, { recursive: true });
  shoot(cover(1600, 900), path.join(outDir, 'kapak.png'), 1600, 900, temp);
  shoot(lookCard('sinema'), path.join(outDir, 'sinema.png'), 1600, 900, temp);
  shoot(lookCard('klasik'), path.join(outDir, 'klasik-kutu.png'), 1600, 900, temp);
  shoot(twoLooks(), path.join(outDir, 'iki-gorunum.png'), 1600, 900, temp);
  shoot(howItWorks(), path.join(outDir, 'nasil-calisir.png'), 1600, 900, temp);
  shoot(cover(1280, 640), path.join(outDir, 'github-onizleme.png'), 1280, 640, temp);
} finally {
  fs.rmSync(temp, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
