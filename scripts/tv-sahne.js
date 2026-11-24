// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen: tanıtım için gerçek TV sayfasını (tv-webos, değiştirilmeden) kayıtlı bir oyun karesinin üstünde çizer. Sahte yerel
// sunucu style, subtitle ve region olaylarını gönderir; başsız Chrome (CDP) 1920×1080 ekran görüntüsü alır. Sahneler:
// [{id, name, subtitle:{text, box}, region}] (region: DiagnosticReplayTests'in yazdığı AD-olay.json). Kareler telif altındadır,
// depoya girmez.
//   node scripts/tv-sahne.js <örnek dizini> <çıktı dizini> <sahneler.json>
'use strict';
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');

const [samples, outDir, scenesFile] = process.argv.slice(2);
const scenes = JSON.parse(fs.readFileSync(scenesFile, 'utf8'));
const tvDir = path.join(__dirname, '..', 'tv-webos');
const PORT = 8798, DEBUG = 9333;
const chrome = [process.env.COUCHSUBS_TEST_BROWSER, process.env.CHROME_BIN, 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/google-chrome', '/usr/bin/chromium'].find(p => p && fs.existsSync(p));
const wait = ms => new Promise(r => setTimeout(r, ms));
const CORS = { 'Access-Control-Allow-Origin': '*', 'Cache-Control': 'no-store' };
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.png': 'image/png', '.jpg': 'image/jpeg', '.json': 'application/json' };

const style = { background: 0, backgroundColor: '#101014', nameOnly: true, backgroundShape: 'fit', bold: true, bottomPercent: 21,
  displayMode: 'overlay', fontSize: 38, outline: true, outlineWidth: 2, placement: 'above', regionsAbove: false, setApart: true,
  stackPrevious: false, textColor: 'white', textHex: '#FFFFFF' };

let current = null;
const streams = new Set();
const send = (res, name, data) => res.write('event: ' + name + '\ndata: ' + JSON.stringify(data) + '\n\n');
function sendScene(res) {
  if (!current) return;
  send(res, 'style', style);
  send(res, 'subtitle', { text: current.subtitle.text, box: current.subtitle.box, duration: 600000 });
  if (current.region) send(res, 'region', current.region);
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  if (req.method === 'OPTIONS') { res.writeHead(204, Object.assign({ 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS', 'Access-Control-Allow-Headers': '*' }, CORS)); return res.end(); }
  if (url.pathname === '/hello') { res.writeHead(200, Object.assign({ 'Content-Type': 'application/json' }, CORS)); return res.end('{"app":"couchsubs","id":"cevirgen-sahne","tvs":0,"live":1}'); }
  if (url.pathname === '/events') {
    res.writeHead(200, Object.assign({ 'Content-Type': 'text/event-stream; charset=utf-8', Connection: 'keep-alive' }, CORS, { 'Cache-Control': 'no-cache' }));
    res.write('retry: 2000\n\n');
    streams.add(res);
    req.on('close', () => streams.delete(res));
    setTimeout(() => sendScene(res), 300);
    return;
  }
  if (url.pathname === '/ack') { res.writeHead(204, CORS); return res.end(); }
  if (url.pathname === '/wrap.html') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    return res.end(`<!doctype html><meta charset="utf-8"><style>html,body{margin:0;width:1920px;height:1080px;overflow:hidden;background:#000}
img{position:absolute;inset:0;width:1920px;height:1080px}iframe{position:absolute;inset:0;width:1920px;height:1080px;border:0;background:transparent}</style>
<img src="/kare/${current.id}-kare.jpg"><script>
try { localStorage.setItem('couchsubs.server', 'http://127.0.0.1:${PORT}'); } catch (e) {}
var f = document.createElement('iframe'); f.allowTransparency = true; f.src = '/tv/index.html';
f.onload = function () { var s = f.contentDocument.createElement('style');
  s.textContent = '#waiting,#no-picture,#demo-badge,#pairing,#source-tip,#panel,#diag,#status,#hint,.toast{display:none!important}html,body{background:transparent!important}';
  f.contentDocument.head.appendChild(s); };
document.body.appendChild(f);
</script>`);
  }
  let file = null;
  if (url.pathname.startsWith('/tv/')) file = path.join(tvDir, decodeURIComponent(url.pathname.slice(4)));
  if (url.pathname.startsWith('/kare/')) file = path.join(samples, decodeURIComponent(url.pathname.slice(6)));
  if (file && fs.existsSync(file)) { res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); return res.end(fs.readFileSync(file)); }
  res.writeHead(404, CORS); res.end();
});

async function cdp(wsUrl) {
  const ws = new WebSocket(wsUrl);
  await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = fail; });
  let id = 0; const pending = new Map();
  ws.onmessage = m => { const d = JSON.parse(m.data); if (d.id && pending.has(d.id)) { pending.get(d.id)(d); pending.delete(d.id); } };
  return { call: (method, params = {}) => new Promise(ok => { const n = ++id; pending.set(n, ok); ws.send(JSON.stringify({ id: n, method, params })); }), close: () => ws.close() };
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  await new Promise(r => server.listen(PORT, '127.0.0.1', r));
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'cevirgen-sahne-'));
  const proc = spawn(chrome, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--hide-scrollbars',
    '--remote-debugging-port=' + DEBUG, '--window-size=1920,1080', '--force-device-scale-factor=1', '--user-data-dir=' + profile, 'about:blank'], { stdio: 'ignore' });
  try {
    let target = null;
    for (let i = 0; i < 50 && !target; i++) {
      await wait(200);
      try { target = (await (await fetch(`http://127.0.0.1:${DEBUG}/json/list`)).json()).find(t => t.type === 'page'); } catch (e) {}
    }
    const page = await cdp(target.webSocketDebuggerUrl);
    await page.call('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
    for (const scene of scenes) {
      current = scene;
      await page.call('Page.navigate', { url: `http://127.0.0.1:${PORT}/wrap.html?${scene.id}` });
      await wait(5000);
      const check = await page.call('Runtime.evaluate', { expression: `(function(){var d=document.querySelector('iframe').contentDocument;return JSON.stringify({sub:d.getElementById('subtitle').className, text:d.getElementById('subtitle-text').textContent, regions:d.getElementById('regions').className, items:d.querySelectorAll('#regions .item').length})})()`, returnByValue: true });
      console.log(scene.id, check.result && check.result.result && check.result.result.value);
      const shot = await page.call('Page.captureScreenshot', { format: 'png' });
      fs.writeFileSync(path.join(outDir, scene.name + '.png'), Buffer.from(shot.result.data, 'base64'));
    }
    page.close();
  } finally {
    proc.kill();
    for (const s of streams) s.end();
    server.close();
    await wait(500);
    try { fs.rmSync(profile, { recursive: true, force: true }); } catch (e) {}
  }
})().catch(e => { console.error(e); process.exit(1); });
