// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen: gerçek Android TV'de ek alan çizimini doğrular. Bilgisayarda yalnız 127.0.0.1'i dinleyen küçük bir sunucu
// açar, TV'deki Çevirgen TV'yi ADB tüneliyle (adb reverse) bu sunucuya yönlendirir, kayıtlı "region" olaylarını
// (DiagnosticReplayTests'in yazdığı AD-olay.json) sırayla gönderir ve her birinde TV'nin gerçekten çizdiği yazıların
// yerini okur: ekran görüntüsü (screencap) alınabiliyorsa onu, alınamıyorsa (HDMI girişi açıkken çoğu TV vermez) TV'nin
// arayüz ağacını (uiautomator dump --windows: katman penceresindeki tarayıcı görünümünün yazı düğümleri ve sınırları).
// Sonuç "tv-canli.json"a yazılır ve her tuş etiketi için beklenen kenarla karşılaştırılır. Bitince çizdiklerini temizler
// ve TV'yi asıl adresine döndürür. Güvenlik duvarına dokunmaz; Çevirgen uygulaması bu sırada kapalı olmalıdır (açıksa
// TV'yi kendi adresine geri çağırır).
//
//   node scripts/tv-live-check.js <adb> <tv:port> <örnek dizini> <çıktı dizini> <geri dönülecek adres> ad1,ad2,...
//
// ADB anahtarı: ADB_VENDOR_KEYS (Çevirgen'in %LOCALAPPDATA%\Cevirgen\adb\adbkey dosyası TV'de zaten onaylıdır).
'use strict';
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const [adb, device, samples, outDir, restore, list] = process.argv.slice(2);
if (!adb || !device || !samples || !outDir || !restore || !list) {
  console.error('Kullanım: node scripts/tv-live-check.js <adb> <tv:port> <örnek dizini> <çıktı dizini> <geri dönülecek adres> ad1,ad2');
  process.exit(2);
}
const PORT = 8799;
const PACKAGE = 'com.cevirgen.tv';
const DUMP = '/sdcard/Download/cevirgen-tv-check.xml';
const names = list.split(',').filter(Boolean);
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const run = (args, options) => spawnSync(adb, ['-s', device, ...args], Object.assign({ encoding: 'utf8', timeout: 30000, windowsHide: true }, options));
const shell = command => run(['shell', command]);
const CORS = { 'Access-Control-Allow-Origin': '*', 'Cache-Control': 'no-store' };

let stream = null;
let connected = null;
const ready = new Promise(resolve => { connected = resolve; });
const server = http.createServer((request, response) => {
  const url = new URL(request.url, 'http://127.0.0.1');
  if (request.method === 'OPTIONS') {
    response.writeHead(204, Object.assign({ 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS', 'Access-Control-Allow-Headers': '*',
      'Access-Control-Allow-Private-Network': 'true' }, CORS));
    response.end();
  } else if (url.pathname === '/hello') {
    // "auth" yok: sayfa eşleştirme sormadan bağlanır.
    response.writeHead(200, Object.assign({ 'Content-Type': 'application/json' }, CORS));
    response.end('{"app":"couchsubs","id":"cevirgen-tv-live-check","tvs":0,"live":1}');
  } else if (url.pathname === '/events') {
    response.writeHead(200, Object.assign({ 'Content-Type': 'text/event-stream; charset=utf-8', Connection: 'keep-alive' }, CORS,
      { 'Cache-Control': 'no-cache' }));
    response.write('retry: 2000\n\n');
    stream = response;
    console.log('TV bağlandı: sürüm ' + url.searchParams.get('v'));
    connected(url.searchParams.get('v'));
  } else if (url.pathname === '/ack') {
    response.writeHead(204, CORS);
    response.end();
  } else {
    response.writeHead(404, CORS);
    response.end();
  }
});
const send = (name, data) => stream.write('event: ' + name + '\ndata: ' + JSON.stringify(data) + '\n\n');

/** TV'nin arayüz ağacından Çevirgen TV penceresindeki yazı düğümleri: [{text, left, top, right, bottom}] (ekran pikseli). */
function textNodes() {
  const dumped = shell('uiautomator dump --windows ' + DUMP + ' >/dev/null 2>&1; cat ' + DUMP);
  const xml = dumped.stdout || '';
  const found = [];
  // Yalnız Çevirgen TV'nin düğümleri (package="com.cevirgen.tv"); yazı "text" ya da "content-desc" alanındadır.
  const pattern = /<node\b[^>]*>/g;
  let match;
  while ((match = pattern.exec(xml)) !== null) {
    const node = match[0];
    if (node.indexOf('package="' + PACKAGE + '"') < 0) continue;
    const attribute = name => { const hit = new RegExp(' ' + name + '="([^"]*)"').exec(node); return hit ? hit[1] : ''; };
    const text = (attribute('text') || attribute('content-desc')).replace(/&amp;/g, '&').replace(/&apos;/g, "'").replace(/&quot;/g, '"')
      .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&#(\d+);/g, (_, code) => String.fromCodePoint(Number(code)));
    const bounds = /\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]/.exec(attribute('bounds'));
    if (!text.trim() || !bounds) continue;
    found.push({ text: text.trim(), left: +bounds[1], top: +bounds[2], right: +bounds[3], bottom: +bounds[4], class: attribute('class') });
  }
  return { nodes: found, size: xml.length };
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  await new Promise(resolve => server.listen(PORT, '127.0.0.1', resolve));
  const reverse = run(['reverse', 'tcp:' + PORT, 'tcp:' + PORT]);
  if (reverse.status !== 0) throw new Error('adb reverse: ' + (reverse.stderr || reverse.stdout));
  const size = /(\d+)x(\d+)\s*$/.exec((shell('wm size').stdout || '').trim().split('\n').pop() || '');
  const screen = size ? { width: +size[1], height: +size[2] } : { width: 1920, height: 1080 };
  const address = JSON.stringify({ server: 'http://127.0.0.1:' + PORT });
  const running = /ServiceRecord\{[^}]*com\.cevirgen\.tv\/\.OverlayService|Hist.*com\.cevirgen\.tv\/\.MainActivity/.test(
    shell('dumpsys activity activities | grep Hist; dumpsys activity services ' + PACKAGE + ' | grep ServiceRecord').stdout || '');
  const command = running
    ? `am broadcast -a ${PACKAGE}.action.ADDRESS -n ${PACKAGE}/.AddressReceiver --es params '${address}'`
    : `am start -n ${PACKAGE}/.MainActivity --es params '${address}'`;
  console.log((running ? 'adres bildirildi: ' : 'uygulama açıldı: ') + (shell(command).stdout || '').trim().split('\n').pop());
  const report = { device, screen, samples: [] };
  const drawn = [];
  let ping = null;
  try {
    const version = await Promise.race([ready, wait(40000).then(() => null)]);
    if (version === null) throw new Error('TV 40 sn içinde bağlanmadı');
    report.version = version;
    ping = setInterval(() => stream.write(': ping\n\nevent: ping\ndata: {}\n\n'), 5000);
    await wait(1200);
    // Erişilebilirlik ağacı ilk okumada kurulur; boş bir ön okuma.
    textNodes();
    for (const name of names) {
      const event = JSON.parse(fs.readFileSync(path.join(samples, name + '-olay.json'), 'utf8'));
      send('region', event);
      drawn.push(event.id);
      await wait(1500);
      const entry = { name, expected: (event.blocks || []).map(block => ({ text: block.text, key: block.key === true,
        align: block.style && block.style.align, left: block.box.left * screen.width, right: block.box.right * screen.width,
        top: block.box.top * screen.height, bottom: block.box.bottom * screen.height })) };
      const shot = run(['exec-out', 'screencap', '-p'], { encoding: 'buffer', maxBuffer: 64 * 1024 * 1024 });
      if (shot.status === 0 && shot.stdout && shot.stdout.length > 1000) {
        fs.writeFileSync(path.join(outDir, name + '-tv-gercek.png'), shot.stdout);
        entry.screenshot = name + '-tv-gercek.png';
      }
      let read = textNodes();
      if (read.nodes.length === 0) { await wait(700); read = textNodes(); }
      entry.nodes = read.nodes;
      entry.dumpSize = read.size;
      report.samples.push(entry);
      console.log(name + ': ' + entry.expected.length + ' blok, ' + entry.nodes.length + ' yazı düğümü' + (entry.screenshot ? ', ekran görüntüsü var' : ''));
      send('region', { id: event.id, text: '' });
      await wait(300);
    }
  } finally {
    if (ping) clearInterval(ping);
    // Çizilenler temizlenir (takılı kalmasın), TV asıl adresine döner (anahtarı o adres için kayıtlıdır), tünel kapanır.
    if (stream) {
      for (const id of new Set(drawn)) send('region', { id, text: '' });
      send('region-clear', {});
      send('clear', {});
      await wait(500);
    }
    console.log('geri: ' + (shell(`am broadcast -a ${PACKAGE}.action.ADDRESS -n ${PACKAGE}/.AddressReceiver --es params '${JSON.stringify({ server: restore })}'`).stdout || '').trim().split('\n').pop());
    await wait(800);
    shell('rm -f ' + DUMP);
    run(['reverse', '--remove', 'tcp:' + PORT]);
    if (stream) stream.end();
    server.close();
    fs.writeFileSync(path.join(outDir, 'tv-canli.json'), JSON.stringify(report, null, 1));
  }
  // Özet: her tuş etiketinin yazısı, yaslandığı kenarda İngilizcenin kenarını geçmemeli.
  let bad = 0, checked = 0;
  for (const sample of report.samples) {
    for (const block of sample.expected.filter(item => item.key)) {
      const node = sample.nodes.find(item => item.text === block.text);
      if (!node) { console.log(`  ${sample.name} "${block.text}": TV'de düğüm yok`); continue; }
      checked++;
      const over = block.align === 'left' ? block.left - node.left : node.right - block.right;
      const lines = Math.round((node.bottom - node.top) / Math.max(1, block.bottom - block.top));
      const ok = over <= 1 && lines <= 1;
      if (!ok) bad++;
      console.log(`  ${sample.name} "${block.text}" ${block.align}: yazı [${node.left}-${node.right}] İngilizce [${Math.round(block.left)}-${Math.round(block.right)}] taşma ${over.toFixed(0)} px ${ok ? 'tamam' : 'SORUN'}`);
    }
  }
  console.log(`özet: ${checked} etiket ölçüldü, ${bad} sorun`);
  if (bad > 0) process.exitCode = 3;
})().catch(error => { console.error(String(error && error.message || error)); process.exitCode = 1; setTimeout(() => process.exit(1), 500); });
