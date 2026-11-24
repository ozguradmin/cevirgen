// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen: tuş etiketlerinin (item.key) gerçek Chromium yerleşimi. node --test scripts/tv-key-label.test.js
// Kutular gerçek GTA 6 karelerinden (2026-11-24): etiketin sağındaki tuş simgesi hiçbir durumda örtülmez.
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { pathToFileURL } = require('node:url');
const root = path.join(__dirname, '..', 'tv-webos');
const browser = [process.env.COUCHSUBS_TEST_BROWSER, process.env.CHROME_BIN,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'].find(p => p && fs.existsSync(p));

const WHITE = { background: '#141519', foreground: '#FFFFFF', flat: true };
const DIM = { background: '#141519', foreground: '#A9ADB6', flat: true };
const key = (text, left, top, right, bottom, extra) => Object.assign({
  text, key: true, line: 0.0305, box: { left, top, right, bottom }, surface: WHITE,
  style: { align: 'right', bold: true, condensed: false, upper: true },
}, extra);
// Dikey liste (REMINDERS □ / SELECT ✕ / EXIT ○): sağ kenarlar hizalı, simge sağda.
const list = [key('HATIRLATICILAR', 0.9029, 0.8086, 0.9614, 0.8404, { room: 0.9614 }),
  key('SEÇ', 0.9245, 0.8822, 0.9614, 0.9120, { room: 0.9614 }),
  key('ÇIKIŞ', 0.9344, 0.9398, 0.9620, 0.9702, { room: 0.9620, surface: DIM })];
// Yatay çubuk (REV [R2]  BUY ✕  BACK ○): ortadaki "SATIN AL" soldaki simgeye değmeden sığmaz.
const bar = [key('DEVİR', 0.8090, 0.9416, 0.8530, 0.9693, { room: 0.8 }),
  key('SATIN AL', 0.8838, 0.9416, 0.9035, 0.9693, { room: 0.0197 }),
  key('GERİ', 0.9255, 0.9416, 0.9620, 0.9693, { room: 0.0365 })];
// İstatistik kartı: sola yaslı etiket sağa büyür.
const card = [key('İSABET ORANI', 0.7630, 0.7516, 0.8172, 0.7809, { room: 0.2, style: { align: 'left', bold: true, condensed: false, upper: true } })];

function render(width, height) {
  const app = fs.readFileSync(path.join(root, 'app.js'), 'utf8');
  const cut = (from, until) => {
    const start = app.indexOf(from);
    const end = app.indexOf('\n  //', app.indexOf(until, start));
    assert.ok(start > 0 && end > start, 'TV işlevi bulunamadı: ' + from);
    return app.slice(start, end);
  };
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'cevirgen-key-test-'));
  try {
    const html = `<!doctype html><meta charset="utf-8"><style>${fs.readFileSync(path.join(root, 'style.css'), 'utf8')}</style>
      <div id="regions" class="hidden"></div><div id="screen"></div><pre id="result"></pre><script>
      function $(id) { return document.getElementById(id); }
      function hideSubtitle() {}
      var diag = { regions: 0, big: 0 };
      ${cut('  function validBox(box)', '  function validBox(box)')}
      ${cut('  var screenTimer = null;', '  function hideScreen()')}
      ${cut('  var regionBoxes = {};', '  function hideRegions()')}
      function measure(el) {
        var b = el.getBoundingClientRect(), cs = getComputedStyle(el);
        return { text: el.textContent, left: b.left, right: b.right, top: b.top, bottom: b.bottom, width: b.width, height: b.height,
          font: parseFloat(cs.fontSize), color: cs.color, background: cs.backgroundColor, display: cs.display,
          scrollWidth: el.scrollWidth, clientWidth: el.clientWidth, lines: Math.round(el.scrollHeight / Math.max(1, b.height)) };
      }
      function draw(id, blocks) {
        showRegion({ id: id, text: blocks.map(function (b) { return b.text; }).join('\\n'),
          box: { left: 0.75, top: 0.7, right: 0.97, bottom: 0.98 }, blocks: blocks });
        var out = Array.from(regionBoxes[id].children).map(measure);
        showRegion({ id: id, text: '' });
        return out;
      }
      var result = { list: draw('user-1', ${JSON.stringify(list)}), bar: draw('user-1', ${JSON.stringify(bar)}),
        card: draw('user-1', ${JSON.stringify(card)}), viewport: { width: innerWidth, height: innerHeight } };
      $('result').textContent = btoa(unescape(encodeURIComponent(JSON.stringify(result))));
      </script>`;
    const encoded = Buffer.from(html, 'utf8').toString('base64');
    const page = `<!doctype html><iframe id="frame" style="border:0;width:${width}px;height:${height}px"></iframe>
      <pre id="result"></pre><script>
      var frame = document.getElementById('frame');
      frame.contentDocument.open();
      frame.contentDocument.write(decodeURIComponent(escape(atob('${encoded}'))));
      frame.contentDocument.close();
      document.getElementById('result').textContent = frame.contentDocument.getElementById('result').textContent;
      </script>`;
    const file = path.join(temp, 'test.html'); fs.writeFileSync(file, page);
    const run = spawnSync(browser, ['--headless=new', '--disable-gpu', '--disable-background-networking', '--no-first-run',
      '--no-default-browser-check', '--hide-scrollbars', '--force-device-scale-factor=1', '--window-size=' + width + ',' + height,
      ...(process.platform === 'linux' ? ['--no-sandbox'] : []),
      '--user-data-dir=' + path.join(temp, 'profile'), '--dump-dom', pathToFileURL(file).href],
      { encoding: 'utf8', timeout: 30000, maxBuffer: 2 * 1024 * 1024, windowsHide: true });
    assert.equal(run.status, 0, String(run.error || run.stderr));
    const match = run.stdout.match(/<pre id="result">([A-Za-z0-9+/=]+)<\/pre>/);
    assert.ok(match, 'Tarayıcı ölçüm üretmedi: ' + run.stdout.slice(-1000));
    return JSON.parse(Buffer.from(match[1], 'base64').toString('utf8'));
  } finally {
    assert.equal(path.dirname(temp), path.resolve(os.tmpdir()));
    fs.rmSync(temp, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  }
}

if (process.env.CI) assert.ok(browser, 'CI: gerçek yerleşim testi için Chrome/Edge gerekli');
for (const [width, height] of [[1280, 720], [1920, 1080], [3840, 2160]]) {
  test('TV tuş etiketleri: ' + width + '×' + height, { skip: !browser && 'Chrome/Edge bulunamadı' }, async t => {
    const r = render(width, height);
    assert.deepEqual(r.viewport, { width, height });
    await t.test('dikey listede plaka tuş simgesinin yanına taşmaz, uzun Türkçe sola büyür', () => {
      assert.equal(r.list.length, 3);
      r.list.forEach((m, i) => {
        const box = list[i].box;
        // Sağ kenar İngilizcenin sağ kenarını geçmez (simge hemen sağındadır).
        assert.ok(m.right <= box.right * width + 0.5, JSON.stringify(m));
        assert.ok(m.right >= box.right * width - 3, JSON.stringify(m));
        // İngilizce satır örtülür.
        assert.ok(m.left <= box.left * width + 2, JSON.stringify(m));
        assert.ok(m.top <= box.top * height + 1 && m.bottom >= box.bottom * height - 1, JSON.stringify(m));
        // Tek satır, yazı taşmaz.
        assert.ok(m.scrollWidth <= m.clientWidth + 1, JSON.stringify(m));
        assert.notEqual(m.display, 'none');
        assert.equal(m.background, 'rgb(20, 21, 25)');
      });
      // "HATIRLATICILAR" "REMINDERS"tan uzundur: sola büyür.
      assert.ok(r.list[0].left < list[0].box.left * width - 4, JSON.stringify(r.list[0]));
      // Listede yazı boyu aynı; renk beyaz ya da soluk gri, koyu değil.
      assert.equal(r.list[0].font, r.list[1].font);
      assert.equal(r.list[1].font, r.list[2].font);
      assert.equal(r.list[0].color, 'rgb(255, 255, 255)');
      assert.equal(r.list[2].color, 'rgb(169, 173, 182)');
    });
    await t.test('yatay çubukta sığmayan etiket çizilmez, sığan küçülmeden kalır', () => {
      const [rev, buy, back] = r.bar;
      assert.notEqual(rev.display, 'none');
      assert.equal(buy.display, 'none', JSON.stringify(buy));
      assert.notEqual(back.display, 'none');
      assert.ok(back.right <= bar[2].box.right * width + 0.5 && back.left >= bar[1].box.right * width - 1, JSON.stringify(back));
      // "GERİ" kendi yerine sığar: en çok bir iki piksel küçülür, okunur kalır.
      assert.ok(back.font >= rev.font * 0.9, JSON.stringify([back, rev]));
    });
    await t.test('sola yaslı etiket sol kenarında durur, sağa büyür', () => {
      const [m] = r.card;
      assert.ok(Math.abs(m.left - card[0].box.left * width) <= 3, JSON.stringify(m));
      assert.ok(m.right > card[0].box.right * width, JSON.stringify(m));
      assert.notEqual(m.display, 'none');
    });
  });
}
