// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen (TV 1.0.2): yazı kenarlığının kalınlığı bilgisayardan gelir (`style.outlineWidth`, 0–4, fontSize ile aynı CSS
// pikseli; 0: kenarlık yok). Küçük üst satırlar (0,8 kat yazı) max(1, round(w × 0,8)) kullanır. Alanı göndermeyen eski
// bilgisayarda kalınlık yazı boyundan hesaplanır (max(2, round(fontSize / 16))). Sayfa (Android TV: i18n.js, platform.js,
// app.js) scripts/tv-live-page.js ile gerçek ağa bağlanır. Çalıştırma: node --test scripts/tv-outline-width.test.js
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const { startPage } = require('./tv-live-page');

const KEY = 'abcdefghijklmnopqrstuv';

function couchsubs() {
  const streams = [];
  const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://x');
    if (url.pathname === '/hello') return res.end(JSON.stringify({ app: 'couchsubs', auth: 1, id: 'outline-test', tvs: streams.length, live: 1 }));
    if (url.pathname === '/pair') return res.end(JSON.stringify({ state: url.searchParams.get('k') === KEY ? 'valid' : 'expired' }));
    if (url.pathname === '/events' && url.searchParams.get('k') === KEY) {
      res.writeHead(200, { 'Content-Type': 'text/event-stream; charset=utf-8' });
      res.write('retry: 2000\n\n');
      streams.push(res);
      req.on('close', () => streams.splice(streams.indexOf(res), 1));
      return undefined;
    }
    res.statusCode = 404;
    return res.end();
  });
  const send = (event, data) => { for (const res of streams) res.write('event: ' + event + '\ndata: ' + JSON.stringify(data) + '\n\n'); };
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve({
    address: 'http://127.0.0.1:' + server.address().port,
    tvs: () => streams.length,
    send,
    close() { for (const res of streams) res.destroy(); server.close(); }
  })));
}

async function until(condition, what, ms = 5000) {
  const deadline = Date.now() + ms;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(what + ' zamanında olmadı');
    await new Promise((r) => setTimeout(r, 20));
  }
}

// app.js'in sekiz yönlü konturu: w px kaydırılmış bulanıksız gölgeler ve w px yumuşak gölge.
function expected(w) {
  const shadows = [];
  for (let dx = -1; dx <= 1; dx++) {
    for (let dy = -1; dy <= 1; dy++) {
      if (dx || dy) shadows.push(dx * w + 'px ' + dy * w + 'px 0 #000');
    }
  }
  shadows.push('0 0 ' + w + 'px #000');
  return shadows.join(', ');
}

const shadow = (page, id) => page.element(id).style.textShadow;

// Her stil iletisini ayrı bir "işaret" yazı boyuyla gönderir; sayfanın iletiyi işlediği yazı boyundan anlaşılır.
async function withPage(fn) {
  const pc = await couchsubs();
  const page = startPage({ params: { server: pc.address, key: KEY }, android8: true, port: 2 });
  try {
    await until(() => pc.tvs() === 1 && page.status() === 'bağlı', 'bağlantı');
    const style = async (data) => {
      pc.send('style', data);
      await until(() => page.element('subtitle-text').style.fontSize === data.fontSize + 'px', 'stilin uygulanması');
    };
    await fn(page, style);
    assert.doesNotMatch(page.diag(), / err /);
  } finally {
    page.close();
    pc.close();
  }
}

test('outlineWidth 0: kenarlık açık olsa da satır içi gölge yok', async () => {
  await withPage(async (page, style) => {
    await style({ fontSize: 46, outline: true, outlineWidth: 3 });
    assert.equal(shadow(page, 'subtitle-text'), expected(3));
    await style({ fontSize: 47, outline: true, outlineWidth: 0 });
    for (const id of ['subtitle-text', 'subtitle-prev-text', 'subtitle-prev2-text']) assert.equal(shadow(page, id), '', id);
  });
});

test('outlineWidth 3: ana satırda 3 px, küçük üst satırlarda round(3 × 0,8) = 2 px', async () => {
  await withPage(async (page, style) => {
    // Yazı boyundan bağımsız: 80 px yazıda eski hesap 5 px verirdi.
    await style({ fontSize: 80, outline: true, outlineWidth: 3 });
    assert.equal(shadow(page, 'subtitle-text'), expected(3));
    assert.match(shadow(page, 'subtitle-text'), /^-3px -3px 0 #000, /);
    assert.equal(shadow(page, 'subtitle-prev-text'), expected(2));
    assert.equal(shadow(page, 'subtitle-prev2-text'), expected(2));
  });
});

test("küçük üst satırlar: max(1, round(w × 0,8)); aralık dışı değer 0–4'e sıkışır", async () => {
  await withPage(async (page, style) => {
    const cases = [[1, 1, 1], [2, 2, 2], [4, 4, 3], [9, 4, 3], [-2, 0, 0]];
    let size = 40;
    for (const [sent, main, small] of cases) {
      await style({ fontSize: size++, outline: true, outlineWidth: sent });
      assert.equal(shadow(page, 'subtitle-text'), main ? expected(main) : '', 'ana ' + sent);
      assert.equal(shadow(page, 'subtitle-prev-text'), small ? expected(small) : '', 'üst ' + sent);
      assert.equal(shadow(page, 'subtitle-prev2-text'), small ? expected(small) : '', 'üst2 ' + sent);
    }
  });
});

test('outlineWidth gelmeyen eski bilgisayar: kalınlık yazı boyundan (max(2, round(fontSize / 16)))', async () => {
  await withPage(async (page, style) => {
    // Önce kalınlık gelir, sonra eski biçim stil: eski hesaba döner.
    await style({ fontSize: 30, outline: true, outlineWidth: 1 });
    assert.equal(shadow(page, 'subtitle-text'), expected(1));
    await style({ fontSize: 46, outline: true });
    assert.equal(shadow(page, 'subtitle-text'), expected(3));
    // Üst satırlar 0,8 kat yazı: round(46 × 0,8) = 37 → round(37 / 16) = 2.
    assert.equal(shadow(page, 'subtitle-prev-text'), expected(2));
    await style({ fontSize: 80, outline: true });
    assert.equal(shadow(page, 'subtitle-text'), expected(5));
    assert.equal(shadow(page, 'subtitle-prev-text'), expected(4));
    await style({ fontSize: 20, outline: true });
    assert.equal(shadow(page, 'subtitle-text'), expected(2));
    assert.equal(shadow(page, 'subtitle-prev-text'), expected(2));
    // Kenarlık kapalıysa kalınlık ne olursa olsun gölge yok.
    await style({ fontSize: 21, outline: false, outlineWidth: 4 });
    assert.equal(shadow(page, 'subtitle-text'), '');
    assert.equal(shadow(page, 'subtitle-prev-text'), '');
  });
});
