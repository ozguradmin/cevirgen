// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen (TV 1.0.1): yığılı altyazının Türkçesi İngilizceyle aynı düzende. Bilgisayar yeni satırı, oyunda hâlâ ekranda
// duran eski repliklerin Türkçesiyle (`subtitle.above`) gönderir; eski İngilizce kalkınca `above` olayıyla kaldırır.
// Altyazıları gizle (`visibility`): gizliyken gelen satır görünmez, gösterilince görünür. Sayfa (Android TV: i18n.js,
// platform.js, app.js) scripts/tv-live-page.js ile gerçek ağa bağlanır. Çalıştırma: node --test scripts/tv-above.test.js
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
    if (url.pathname === '/hello') return res.end(JSON.stringify({ app: 'couchsubs', auth: 1, id: 'above-test', tvs: streams.length, live: 1 }));
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

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function slot(page, id) {
  const box = page.element(id);
  const text = page.element(id + '-text');
  return box.className === 'hidden' ? '' : String(text.rawText !== undefined ? text.rawText : text.textContent);
}

test('üst satırlar bilgisayarın söylediğine göre durur, süreyle solmaz', async () => {
  const pc = await couchsubs();
  const page = startPage({ params: { server: pc.address, key: KEY }, android8: true, port: 2 });
  try {
    await until(() => pc.tvs() === 1 && page.status() === 'bağlı', 'bağlantı');
    pc.send('subtitle', { text: 'Bu tuvaleti buraya kim yaptı?', duration: 15000, above: [] });
    await until(() => page.shown() === 'Bu tuvaleti buraya kim yaptı?', 'ilk satır');
    assert.equal(slot(page, 'subtitle-prev'), '');

    pc.send('subtitle', { text: 'Depo alanı asla fazla olmaz.', duration: 15000, above: ['Bu tuvaleti buraya kim yaptı?'] });
    await until(() => page.shown() === 'Depo alanı asla fazla olmaz.', 'ikinci satır');
    assert.equal(slot(page, 'subtitle-prev'), 'Bu tuvaleti buraya kim yaptı?');
    // Eski süreli yığın 1,5 sn + 0,4 sn sonra kaldırırdı; bilgisayar söylemedikçe kalır.
    await sleep(2200);
    assert.equal(slot(page, 'subtitle-prev'), 'Bu tuvaleti buraya kim yaptı?');
    assert.doesNotMatch(page.element('subtitle-prev').className, /fading/);

    // Üç satır: en eskisi en üstte.
    pc.send('subtitle', { text: 'Haklısın dostum.', duration: 15000, above: ['Bir.', 'İki.'] });
    await until(() => page.shown() === 'Haklısın dostum.', 'üçüncü satır');
    assert.equal(slot(page, 'subtitle-prev2'), 'Bir.');
    assert.equal(slot(page, 'subtitle-prev'), 'İki.');

    // Eski İngilizce kalktı: Türkçesi kalkar, satırın kendisi kalır.
    pc.send('above', { lines: ['İki.'] });
    await until(() => slot(page, 'subtitle-prev2') === '', 'en üstteki satırın kalkması');
    assert.equal(slot(page, 'subtitle-prev'), 'İki.');
    pc.send('above', { lines: [] });
    await until(() => slot(page, 'subtitle-prev') === '', 'üst satırın kalkması');
    assert.equal(page.shown(), 'Haklısın dostum.');

    // Temizle: hepsi kalkar.
    pc.send('subtitle', { text: 'Son satır.', duration: 15000, above: ['Haklısın dostum.'] });
    await until(() => page.shown() === 'Son satır.', 'son satır');
    pc.send('clear', {});
    await until(() => page.shown() === '' && slot(page, 'subtitle-prev') === '', 'temizleme');
    assert.doesNotMatch(page.diag(), / err /);
  } finally {
    page.close();
    pc.close();
  }
});

test('above gelmeyen eski bilgisayarla süreli yığın sürer', async () => {
  const pc = await couchsubs();
  const page = startPage({ params: { server: pc.address, key: KEY }, android8: true, port: 2 });
  try {
    await until(() => pc.tvs() === 1 && page.status() === 'bağlı', 'bağlantı');
    pc.send('style', { stackPrevious: true, fontSize: 46, placement: 'fixed' });
    pc.send('subtitle', { text: 'Yere yat!', duration: 4000 });
    await until(() => page.shown() === 'Yere yat!', 'ilk satır');
    pc.send('subtitle', { text: 'Kapıyı tut!', duration: 4000 });
    await until(() => page.shown() === 'Kapıyı tut!', 'ikinci satır');
    assert.equal(slot(page, 'subtitle-prev'), 'Yere yat!');
    await until(() => slot(page, 'subtitle-prev') === '', 'süreyle kalkma', 4000);
  } finally {
    page.close();
    pc.close();
  }
});

test('gizliyken gelen satır görünmez, gösterilince görünür', async () => {
  const pc = await couchsubs();
  const page = startPage({ params: { server: pc.address, key: KEY }, android8: true, port: 2 });
  try {
    await until(() => pc.tvs() === 1 && page.status() === 'bağlı', 'bağlantı');
    pc.send('visibility', { hidden: true });
    await until(() => page.element('subtitle').style.visibility === 'hidden', 'gizleme');
    pc.send('subtitle', { text: 'Gizliyken gelen satır.', duration: 15000, above: [] });
    await until(() => page.shown() === 'Gizliyken gelen satır.', 'satırın çizilmesi');
    // Satır çizildi ama katman görünmez: yeni satır gizlemeyi bozmaz.
    for (const id of ['subtitle', 'subtitle-cover', 'regions', 'screen']) assert.equal(page.element(id).style.visibility, 'hidden', id);
    pc.send('visibility', { hidden: false });
    await until(() => page.element('subtitle').style.visibility === '', 'gösterme');
    assert.equal(page.shown(), 'Gizliyken gelen satır.');
    assert.doesNotMatch(page.diag(), / err /);
  } finally {
    page.close();
    pc.close();
  }
});
