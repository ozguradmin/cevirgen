// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen API (Cloudflare Worker):
//   GET  /v1/config          uygulamanın uzaktan ayarları: reklamlar, açılır reklam, duyuru, oyun kapakları, destek alanı
//                            (KV "config"); güncelleme gerektirmez
//   GET  /v1/assets/<ad>     reklam görselleri (KV "asset:<ad>")
//   POST /v1/ping            anonim sinyal {id, v, p, playing}; IP ve içerik saklanmaz
//   POST /v1/lines           ortak havuz (yalnız toplama, src/lines.js): ekranda gösterilen İngilizce satırlar, modeldeki
//                            çevirileri ve oyun {id, v, game:{id?, title?}, lines:[{en, tr, engine, area, at, n?}]};
//                            süzülür (oyun dışı yazı, tuş ipucu, kişisel bilgi), kurulum kimliği yalnız tuzlu özet olarak
//                            yazılır, kurulum başına günde 2000 satır
//   GET  /admin              yönetim paneli (Basic auth, şifre ADMIN_TOKEN secret'ı)
//   GET  /admin/stats.json   istatistikler; POST /admin/config, POST /admin/asset
//   GET  /admin/lines/games.json        havuz: oyun başına satır, ≥2 kurulumun gördüğü satır, katkı veren kurulum, son 7 gün
//   GET  /admin/lines.json?game=&q=&limit=&offset=   bir oyunun satırları (en çok görülen başta) ve motor başına çeviriler
//   GET  /admin/lines.csv?game=         bir oyunun bütün satırları CSV olarak
//   Cron (her gece 02:23 UTC): 180 günden eski kurulum–satır eşleşmeleri ve 7 günden eski günlük sayaçlar silinir.

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const PLATFORMS = new Set(['ps5', 'xbox', 'pc', 'capture', 'unknown']);
const DEFAULT_CONFIG = { version: 1, ads: [], popups: [], announcement: null };
// Oyun kimliği uygulamanın oyun/paket kimliğiyle aynı (gta-6, rdr-2).
const GAME_ID = /^[a-z0-9-]{1,64}$/;
const HTTPS = /^https:\/\/[^\s/]+\S*$/;
const MAX_GAMES = 500;
const ONLINE_SECONDS = 6 * 60; // uygulama 5 dakikada bir sinyal yollar
const REPO = 'ozguradmin/cevirgen';

import { gamesSummary, linesCsv, linesList, pruneLines, submitLines, unavailableFor } from './lines.js';

const json = (body, status = 200, extra = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', ...extra } });

// Türkiye saatiyle gün (UTC+3, yaz saati yok).
const trDay = (unix) => new Date((unix + 3 * 3600) * 1000).toISOString().slice(0, 10);

function authorized(request, env) {
  if (!env.ADMIN_TOKEN) return false;
  const header = request.headers.get('authorization') || '';
  if (!header.startsWith('Basic ')) return false;
  let decoded = '';
  try { decoded = atob(header.slice(6)); } catch { return false; }
  const password = decoded.slice(decoded.indexOf(':') + 1);
  if (password.length !== env.ADMIN_TOKEN.length) return false;
  let diff = 0;
  for (let i = 0; i < password.length; i++) diff |= password.charCodeAt(i) ^ env.ADMIN_TOKEN.charCodeAt(i);
  return diff === 0;
}

const unauthorized = () => new Response('Yetki gerekli', { status: 401, headers: { 'www-authenticate': 'Basic realm="Cevirgen", charset="UTF-8"' } });

async function ping(request, env) {
  let body;
  try { body = await request.json(); } catch { return json({ error: 'json' }, 400); }
  const id = String(body?.id || '').toLowerCase();
  const version = String(body?.v || '').slice(0, 24);
  const platform = PLATFORMS.has(body?.p) ? body.p : 'unknown';
  const playing = body?.playing === true ? 1 : 0;
  if (!UUID.test(id) || !/^[0-9A-Za-z.\-+]{1,24}$/.test(version)) return json({ error: 'gecersiz' }, 400);
  const now = Math.floor(Date.now() / 1000);
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO installs (id, first_seen, last_seen, version, platform, playing) VALUES (?1, ?2, ?2, ?3, ?4, ?5)
                    ON CONFLICT(id) DO UPDATE SET last_seen = ?2, version = ?3, platform = ?4, playing = ?5`)
      .bind(id, now, version, platform, playing),
    env.DB.prepare(`INSERT INTO days (day, id, played) VALUES (?1, ?2, ?3)
                    ON CONFLICT(day, id) DO UPDATE SET played = MAX(played, ?3)`).bind(trDay(now), id, playing),
  ]);
  return new Response(null, { status: 204 });
}

async function stats(env) {
  const now = Math.floor(Date.now() / 1000);
  const today = trDay(now);
  const first = (sql, ...args) => env.DB.prepare(sql).bind(...args).first();
  const all = (sql, ...args) => env.DB.prepare(sql).bind(...args).all().then((r) => r.results);
  const since30 = trDay(now - 29 * 86400);
  const [online, playingNow, dau, playedToday, total, newToday, series, platforms, versions] = await Promise.all([
    first('SELECT COUNT(*) n FROM installs WHERE last_seen >= ?1', now - ONLINE_SECONDS),
    first('SELECT COUNT(*) n FROM installs WHERE last_seen >= ?1 AND playing = 1', now - ONLINE_SECONDS),
    first('SELECT COUNT(*) n FROM days WHERE day = ?1', today),
    first('SELECT COUNT(*) n FROM days WHERE day = ?1 AND played = 1', today),
    first('SELECT COUNT(*) n FROM installs'),
    first('SELECT COUNT(*) n FROM installs WHERE first_seen >= ?1', Math.floor(Date.parse(today + 'T00:00:00+03:00') / 1000)),
    all('SELECT day, COUNT(*) users, SUM(played) players FROM days WHERE day >= ?1 GROUP BY day ORDER BY day', since30),
    all('SELECT platform, COUNT(*) n FROM installs WHERE last_seen >= ?1 GROUP BY platform ORDER BY n DESC', now - 30 * 86400),
    all('SELECT version, COUNT(*) n FROM installs WHERE last_seen >= ?1 GROUP BY version ORDER BY n DESC LIMIT 8', now - 30 * 86400),
  ]);
  let downloads = null;
  try {
    const headers = { 'user-agent': 'cevirgen-api', accept: 'application/vnd.github+json' };
    if (env.GITHUB_TOKEN) headers.authorization = `Bearer ${env.GITHUB_TOKEN}`;
    const r = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=100`, { headers, cf: { cacheTtl: 600 } });
    if (r.ok) {
      const releases = await r.json();
      downloads = releases.reduce((sum, rel) => sum + rel.assets.filter((a) => /setup|\.exe$|full\.nupkg$/i.test(a.name))
        .reduce((s, a) => s + a.download_count, 0), 0);
    }
  } catch { downloads = null; }
  return {
    at: now, online: online.n, playingNow: playingNow.n, today: dau.n, playedToday: playedToday.n, total: total.n,
    newToday: newToday.n, downloads, series, platforms, versions,
  };
}

async function getConfig(env) {
  const raw = await env.CONFIG.get('config');
  try { return raw ? JSON.parse(raw) : DEFAULT_CONFIG; } catch { return DEFAULT_CONFIG; }
}

// Uygulamanın okuduğu biçim: reklam ve açılır reklam bağlantıları yalnız https; görseller /v1/assets altından.
function validateConfig(c) {
  if (!c || typeof c !== 'object' || Array.isArray(c)) return 'Kök bir nesne olmalı.';
  for (const key of ['ads', 'popups']) if (c[key] !== undefined && !Array.isArray(c[key])) return `${key} bir liste olmalı.`;
  for (const ad of [...(c.ads || []), ...(c.popups || [])]) {
    if (ad.link && !/^https:\/\//.test(ad.link)) return `Bağlantı https ile başlamalı: ${ad.link}`;
  }
  if (c.announcement && typeof c.announcement !== 'object') return 'announcement bir nesne ya da null olmalı.';
  return validateGames(c.games) || validateSupport(c.support);
}

// Canlı ekrandaki destek alanı: {id?, text, button, link, icon?}. null ya da yoksa uygulama kendi metnini gösterir
// ("GitHub'da yıldız ver"). Yıldız karşılığında bir şey vaat edilmemeli (GitHub teşvikli yıldızı yasaklar).
function validateSupport(support) {
  if (support === undefined || support === null) return null;
  if (typeof support !== 'object' || Array.isArray(support)) return 'support bir nesne ya da null olmalı.';
  if (typeof support.text !== 'string' || !support.text.trim() || support.text.length > 200) return 'support.text gerekli (en çok 200 karakter).';
  if (typeof support.button !== 'string' || !support.button.trim() || support.button.length > 40) return 'support.button gerekli (en çok 40 karakter).';
  if (typeof support.link !== 'string' || !HTTPS.test(support.link)) return 'support.link https ile başlamalı.';
  if (support.icon !== undefined && support.icon !== null && (typeof support.icon !== 'string' || !HTTPS.test(support.icon)))
    return 'support.icon https ile başlamalı.';
  if (support.id !== undefined && (typeof support.id !== 'string' || support.id.length > 64)) return 'support.id en çok 64 karakterlik bir yazı olmalı.';
  return null;
}

// "Hangi oyunu oynuyorsun?" seçicisinin kapakları: {id, title, cover?, aliases?}. Kapak yalnız https (uygulama en çok 2 MB
// indirir, görsel imzasını denetler, diske önbelleğe alır); kullanıcının kendi kapağı her zaman önce gelir.
function validateGames(games) {
  if (games === undefined || games === null) return null;
  if (!Array.isArray(games)) return 'games bir liste olmalı.';
  if (games.length > MAX_GAMES) return `games en çok ${MAX_GAMES} oyun olabilir.`;
  const ids = new Set();
  for (const game of games) {
    if (!game || typeof game !== 'object' || Array.isArray(game)) return 'games içindeki her oyun bir nesne olmalı.';
    const id = game.id;
    if (typeof id !== 'string' || !GAME_ID.test(id)) return `Oyun kimliği yalnız küçük harf, rakam ve tire olmalı (1–64): ${id}`;
    if (ids.has(id)) return `Aynı oyun kimliği iki kez: ${id}`;
    ids.add(id);
    if (typeof game.title !== 'string' || !game.title.trim() || game.title.length > 120) return `Oyunun adı gerekli (en çok 120 karakter): ${id}`;
    if (game.cover !== undefined && game.cover !== null && (typeof game.cover !== 'string' || !HTTPS.test(game.cover)))
      return `Kapak adresi https ile başlamalı: ${id}`;
    if (game.aliases !== undefined && game.aliases !== null) {
      if (!Array.isArray(game.aliases) || game.aliases.length > 20) return `aliases en çok 20 addan oluşan bir liste olmalı: ${id}`;
      if (game.aliases.some((alias) => typeof alias !== 'string' || !alias.trim() || alias.length > 120)) return `aliases içinde boş ya da çok uzun ad var: ${id}`;
    }
  }
  return null;
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;
    if (request.method === 'GET' && path === '/v1/config') {
      return json(await getConfig(env), 200, { 'cache-control': 'public, max-age=300' });
    }
    if (request.method === 'GET' && path.startsWith('/v1/assets/')) {
      const name = decodeURIComponent(path.slice('/v1/assets/'.length));
      const { value, metadata } = await env.CONFIG.getWithMetadata('asset:' + name, { type: 'arrayBuffer' });
      if (!value) return new Response('Yok', { status: 404 });
      return new Response(value, { headers: { 'content-type': metadata?.type || 'application/octet-stream', 'cache-control': 'public, max-age=3600' } });
    }
    if (request.method === 'POST' && path === '/v1/ping') return ping(request, env);
    if (request.method === 'POST' && path === '/v1/lines') {
      try {
        return await submitLines(request, env);
      } catch (error) {
        console.error(JSON.stringify({ error: 'lines', message: String(error?.message || error).slice(0, 300) }));
        const wait = unavailableFor(error);
        return json({ error: wait ? 'unavailable' : 'server_error' }, wait ? 503 : 500, wait ? { 'retry-after': String(wait) } : {});
      }
    }

    if (path === '/admin' || path.startsWith('/admin/')) {
      if (!authorized(request, env)) return unauthorized();
      if (request.method === 'GET' && path === '/admin') return new Response(ADMIN_HTML, { headers: { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' } });
      if (request.method === 'GET' && path === '/admin/stats.json') return json(await stats(env), 200, { 'cache-control': 'no-store' });
      if (request.method === 'GET' && path === '/admin/lines/games.json') return json(await gamesSummary(env), 200, { 'cache-control': 'no-store' });
      if (request.method === 'GET' && path === '/admin/lines.json') return linesList(url, env);
      if (request.method === 'GET' && path === '/admin/lines.csv') return linesCsv(url, env);
      if (request.method === 'GET' && path === '/admin/config') return json(await getConfig(env), 200, { 'cache-control': 'no-store' });
      if (request.method === 'POST' && path === '/admin/config') {
        let c;
        try { c = await request.json(); } catch { return json({ error: 'JSON okunamadı.' }, 400); }
        const problem = validateConfig(c);
        if (problem) return json({ error: problem }, 400);
        await env.CONFIG.put('config', JSON.stringify(c));
        return json({ ok: true });
      }
      if (request.method === 'POST' && path === '/admin/asset') {
        const form = await request.formData();
        const file = form.get('file');
        const name = String(form.get('name') || file?.name || '').replace(/[^A-Za-z0-9._-]/g, '-').slice(0, 80);
        if (!file || !name || !/^image\/(png|jpeg|webp|gif)$/.test(file.type)) return json({ error: 'PNG, JPG, WEBP ya da GIF seç.' }, 400);
        if (file.size > 2 * 1024 * 1024) return json({ error: 'Görsel en çok 2 MB olabilir.' }, 400);
        await env.CONFIG.put('asset:' + name, await file.arrayBuffer(), { metadata: { type: file.type } });
        return json({ ok: true, url: `${url.origin}/v1/assets/${encodeURIComponent(name)}` });
      }
      return new Response('Yok', { status: 404 });
    }
    return new Response('Çevirgen API', { headers: { 'content-type': 'text/plain; charset=utf-8' } });
  },

  // Saklama süresi (wrangler.toml [triggers]): ham kurulum–satır eşleşmeleri 180 gün tutulur; toplam sayılar kalır.
  // TODO(paketler): ileride gece paketi (installs >= 2 satırları çevir, gözden geçir, imzala, yayınla) buraya eklenecek.
  async scheduled(event, env, ctx) {
    ctx.waitUntil(pruneLines(env).then((r) => console.log(JSON.stringify({ prune: r }))));
  },
};

import ADMIN_HTML from './admin.html';
