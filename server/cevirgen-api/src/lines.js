// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen ortak havuzu (yalnız toplama): uygulama ekranda gösterdiği İngilizce satırları ve o satırın modeldeki
// çevirisini oyunun adıyla gönderir; burada süzülür ve oyun + İngilizce anahtarı başına tek satırda toplanır.
// Süzgeç CouchSubs havuzununki (filter.js, prompts.js; AGPL-3.0) artı kişisel bilgi süzgecidir (aşağıda). Anahtar
// CouchSubs'ın canon() + SHA-256 ilk 8 baytı (server/pool/src/index.js; uygulamadaki PackKey ile aynı).
//
// Çeviri ve paket üretimi YOK (bkz. README.md "Paketler nasıl çıkacak"); TODO(paketler) işaretli yerde başlayacak.

import { accepts } from './filter.js';
import { EXTRA_SUFFIX, gameKey, gameSlug } from './games.js';

export const MAX_LINES = 200;
export const MAX_BODY = 256 * 1024;
export const DAILY_LIMIT = 2000;
export const MAX_EN = 300;
export const MAX_TR = 600;
export const MAX_TITLE = 120;
export const MAX_ENGINE = 48;
export const RETENTION_DAYS = 180;
export const QUOTA_KEEP_DAYS = 7;
const AREAS = new Set(['subtitle', 'extra', 'screen']);
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const VERSION = /^[0-9A-Za-z.\-+]{1,24}$/;
const GAME_ID = /^[a-z0-9-]{1,64}$/;
const TAG = /<[^<>]{0,40}>/g;
// Kurulum kimliği tabloya düz yazılmaz: tuzlanmış SHA-256'nın ilk 8 baytı. Tuz sonradan değişirse "kaç kurulum gördü"
// sayıları kırılır; bu yüzden secret yoksa sabit tuz kullanılır (UUID v4 zaten tahmin edilemez).
const DEFAULT_PEPPER = 'cevirgen-lines-v1';

const json = (body, status = 200, extra = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', ...extra } });

// --- Metin ---

/** Denetim karakterleri atılır, boşluklar sadeleşir. */
export function clean(value, max) {
  if (typeof value !== 'string') return '';
  const text = value.replace(/\p{C}/gu, ' ').replace(/\s+/g, ' ').trim();
  return [...text].length > max ? '' : text;
}

/** CouchSubs'ın anahtar işlevi (server/pool/src/index.js canon; uygulamadaki PackKey.Canon). */
export function canon(s) {
  let t = s.replace(TAG, ' ').normalize('NFKC').toLowerCase().normalize('NFD');
  t = t.replace(/\p{M}/gu, '').replace(/[^\p{L}\p{N}]/gu, ' ');
  return t.split(/\s+/).filter(Boolean).join(' ');
}

const hex = (buffer, bytes) => [...new Uint8Array(buffer)].slice(0, bytes).map((b) => b.toString(16).padStart(2, '0')).join('');

export async function lineKey(text) {
  const c = canon(text);
  if (!c) return '';
  return hex(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(c)), 8);
}

export async function installHash(id, env) {
  const pepper = env?.LINES_PEPPER || DEFAULT_PEPPER;
  return hex(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${pepper}:${id}`)), 8);
}

// --- Kişisel bilgi süzgeci (tutucu: şüpheli satır hiç yazılmaz) ---

const EMAIL = /[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(\.[\p{L}\p{N}-]+)+/u;
const HANDLE = /(^|[^\p{L}\p{N}])@[\p{L}\p{N}_.]{2,}/u;
const URL_LIKE = /(https?:\/\/|\bwww\.|\b[\p{L}\p{N}-]+\.(com|net|org|io|gg|tv|me|co|tr|de|uk|ru|info|biz|xyz|app|dev|ly|be|to|fm|link|site|online|store|shop)\b)/iu;
const HASH_NUMBER = /#\s?\d/;
// Rakamları ve aralarındaki boşluk, tire, nokta, parantez dizisi: en az 7 rakam telefon numarası sayılır.
const DIGIT_RUN = /\+?\(?\d[\d\s().\-]{5,}\d/g;
// Harf ve rakam karışık ama oyun dilinde olağan sözcükler: sıra sayısı, on yıl, saat, birim, para ("3rd", "1990s",
// "10pm", "9mm", "500k", "4x4").
const ORDINARY_MIXED = /^\$?\d+([.,]\d+)?(s|st|nd|rd|th|am|pm|k|m|bn|km|mph|kmh|kg|lb|lbs|ft|mm|cm|hp|x|%|°|gb|mb)$/i;
const DIMENSION = /^\d+x\d+$/i;

/** Kullanıcı adı / PSN kimliği gibi sözcük: alt çizgi taşır ya da harfle birlikte en az iki rakam ("Player1234", "xX_Gamer_99"). */
export function looksLikeHandle(token) {
  const t = token.replace(/^[^\p{L}\p{N}_]+|[^\p{L}\p{N}_]+$/gu, '');
  if (!t) return false;
  if (t.includes('_') && /\p{L}|\p{N}/u.test(t.replace(/_/g, ''))) return true;
  const letters = (t.match(/\p{L}/gu) || []).length;
  const digits = (t.match(/\p{N}/gu) || []).length;
  if (letters === 0 || digits < 2) return false;
  return !ORDINARY_MIXED.test(t) && !DIMENSION.test(t);
}

/** E-posta, bağlantı, telefon, @ad, #numara ya da kullanıcı adına benzeyen sözcük taşıyor mu. */
export function hasPersonalData(text) {
  if (EMAIL.test(text) || HANDLE.test(text) || URL_LIKE.test(text) || HASH_NUMBER.test(text)) return true;
  for (const run of text.match(DIGIT_RUN) || []) if ((run.match(/\d/g) || []).length >= 7) return true;
  return text.split(/\s+/).some(looksLikeHandle);
}

const fold = (text) => text.toLowerCase().replace(/ı/g, 'i').normalize('NFD').replace(/\p{M}/gu, '')
  .split(/[^\p{L}\p{N}]+/u).filter(Boolean);

/** PS5'in "Uzaktan Oynatma'ya bağlandı" / "connected to Remote Play" bildirimi (yazı tanıma bozmalarıyla). */
export function isRemotePlayNotice(text) {
  const words = fold(text);
  for (let i = 0; i < words.length; i++) {
    if (/^rem[o0]te(p[li1]ay)?$/.test(words[i]) && (words[i].length > 6 || /^p[li1]ay/.test(words[i + 1] ?? ''))) return true;
  }
  const remote = words.some((w) => /^u?z?aktan$/.test(w) || w === 'uzakta');
  const play = words.some((w) => /^oyna(t|m)?(ma|a)?/.test(w) || w.startsWith('oynat'));
  return remote && play;
}

/** İngilizce satır havuza girer mi (CouchSubs süzgeci + kişisel bilgi + Remote Play bildirimi). */
export function acceptsEnglish(en) {
  if (!en || [...en].length > MAX_EN || !/\p{L}/u.test(en)) return false;
  return accepts(en) && !hasPersonalData(en) && !isRemotePlayNotice(en);
}

/** Çeviri yazılabilir mi: boş değil, uzun değil, kişisel bilgi yok. */
export function acceptsTurkish(tr) {
  return !!tr && [...tr].length <= MAX_TR && !hasPersonalData(tr) && !isRemotePlayNotice(tr);
}

/** Motor etiketi: küçük harf, [a-z0-9._:/-], en çok 48 karakter; boşsa null. */
export function engineLabel(value) {
  if (typeof value !== 'string') return null;
  const t = value.toLowerCase().replace(/[^a-z0-9._:/-]/g, '').slice(0, MAX_ENGINE);
  return t || null;
}

/** Oyun adı: denetim karakterleri atılır, en çok 120 karakter, ek paketin " (ek satırlar)" eki atılır; boşsa null. */
export function gameTitle(value) {
  if (typeof value !== 'string') return null;
  let t = value.replace(/\p{C}/gu, ' ').replace(/\s+/g, ' ').trim();
  const extra = EXTRA_SUFFIX.trim();
  if (t.endsWith(extra)) t = t.slice(0, -extra.length).trim();
  t = t.slice(0, MAX_TITLE).trim();
  return t || null;
}

/** Oyunun kayıt anahtarı: geçerli kimlik, yoksa adın anahtarı ("Grand Theft Auto VI" → "gta-6"), yoksa "unknown". */
export function gameOf(game) {
  const title = gameTitle(game?.title);
  const id = typeof game?.id === 'string' && GAME_ID.test(game.id) ? game.id : null;
  const slug = id ?? (title ? gameSlug(gameKey(title)) : '');
  return { game: slug || 'unknown', title };
}

/** Satırın zamanı (unix sn): geçersizse şimdi; gelecekte ya da 30 günden eskiyse sınıra çekilir. */
export function lineTime(value, now) {
  const at = typeof value === 'string' ? Math.floor(Date.parse(value) / 1000) : NaN;
  if (!Number.isFinite(at)) return now;
  return Math.min(now, Math.max(now - 30 * 86400, at));
}

/**
 * Gönderinin satırları süzülür ve anahtar başına birleştirilir. Dönen her öğe: { k, en, tr, engine, at, n }.
 * Aynı istekte aynı anahtar birden çok gelirse görülme sayıları toplanır, en son çeviri kalır.
 */
export async function prepare(lines, now) {
  const merged = new Map();
  for (const line of lines) {
    if (!line || typeof line !== 'object') continue;
    if (line.area !== undefined && !AREAS.has(line.area)) continue;
    const en = clean(line.en, MAX_EN);
    if (!acceptsEnglish(en)) continue;
    const engine = engineLabel(line.engine);
    if (!engine) continue;
    const tr = clean(line.tr, MAX_TR);
    // Çeviride kişisel bilgi varsa satırın tamamı atılır (İngilizcesi süzgeçten kaçmış olabilir).
    if (tr && (hasPersonalData(tr) || isRemotePlayNotice(tr))) continue;
    const k = await lineKey(en);
    if (!k || canon(en).split(' ').length < 2) continue;
    const n = Number.isInteger(line.n) ? Math.min(50, Math.max(1, line.n)) : 1;
    const at = lineTime(line.at, now);
    const translations = new Map();
    if (acceptsTurkish(tr)) translations.set(engine, tr);
    const previous = merged.get(k);
    if (previous) {
      previous.n = Math.min(50, previous.n + n);
      previous.at = Math.max(previous.at, at);
      for (const [e, t] of translations) previous.translations.set(e, t);
    } else {
      merged.set(k, { k, en, at, n, translations });
    }
  }
  return [...merged.values()];
}

// --- Gönderim ---

function untilMidnight(now) {
  const date = new Date(now * 1000);
  const midnight = Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + 1) / 1000;
  return Math.max(60, Math.ceil(midnight - now));
}

const utcDay = (unix) => new Date(unix * 1000).toISOString().slice(0, 10);

// D1 tek ifadede en çok 100 değişken kabul eder ve ücretsiz planda çağrı başına sorgu sayısı sınırlıdır: satırlar tek
// tek değil, JSON dizisi olarak tek değişkenle gider (json_each). Bir gönderi en çok ~6 sorgu.
const SEEN_SQL =
  "INSERT OR IGNORE INTO pool_seen(game, key, install, at) SELECT ?1, value, ?2, ?3 FROM json_each(?4)";
// Kaç kurulum gördü: pool_seen'deki sayım (az önce yazılan dahil). Eski görülmeler silinse de sayı düşmez (MAX).
const LINES_SQL =
  "INSERT INTO pool_lines(game, key, en, first_seen, last_seen, installs, sightings) " +
  "SELECT ?1, j.k, j.en, j.at, j.at, (SELECT COUNT(*) FROM pool_seen s WHERE s.game = ?1 AND s.key = j.k), j.n FROM (" +
  "SELECT json_extract(value, '$.k') AS k, json_extract(value, '$.en') AS en, json_extract(value, '$.at') AS at, " +
  "json_extract(value, '$.n') AS n FROM json_each(?2)) AS j WHERE 1 " +
  "ON CONFLICT(game, key) DO UPDATE SET last_seen = MAX(pool_lines.last_seen, excluded.last_seen), " +
  "installs = MAX(pool_lines.installs, excluded.installs), sightings = pool_lines.sightings + excluded.sightings";
// Motor başına en son çeviri; aynı çeviri yeniden yazılmaz (D1 yazım kotası).
const TRANSLATIONS_SQL =
  "INSERT INTO pool_translations(game, key, engine, tr, at) " +
  "SELECT ?1, json_extract(value, '$.k'), json_extract(value, '$.e'), json_extract(value, '$.t'), ?3 FROM json_each(?2) WHERE 1 " +
  "ON CONFLICT(game, key, engine) DO UPDATE SET tr = excluded.tr, at = excluded.at WHERE pool_translations.tr <> excluded.tr";
const GAMES_SQL =
  "INSERT INTO pool_games(game, title, first_seen, last_seen) VALUES (?1, ?2, ?3, ?3) " +
  "ON CONFLICT(game) DO UPDATE SET title = COALESCE(excluded.title, pool_games.title), last_seen = excluded.last_seen " +
  "WHERE pool_games.last_seen < excluded.last_seen - 3600 OR (excluded.title IS NOT NULL AND pool_games.title IS NOT excluded.title)";

export async function submitLines(request, env) {
  const length = Number(request.headers.get('content-length') || 0);
  if (length > MAX_BODY) return json({ error: 'cok_buyuk' }, 413);
  const raw = await request.text();
  if (new TextEncoder().encode(raw).length > MAX_BODY) return json({ error: 'cok_buyuk' }, 413);
  let body;
  try { body = JSON.parse(raw); } catch { return json({ error: 'json' }, 400); }
  const id = String(body?.id || '').toLowerCase();
  const version = String(body?.v || '');
  if (!UUID.test(id) || !VERSION.test(version) || !Array.isArray(body?.lines)) return json({ error: 'gecersiz' }, 400);
  if (body.game !== undefined && body.game !== null && (typeof body.game !== 'object' || Array.isArray(body.game)))
    return json({ error: 'gecersiz' }, 400);
  const now = Math.floor(Date.now() / 1000);
  const lines = body.lines.slice(0, MAX_LINES);
  const install = await installHash(id, env);
  const day = utcDay(now);

  // Günlük sınır: kurulum başına DAILY_LIMIT satır (süzülmeden önceki sayı). Dolduysa yazım yapılmaz.
  const used = (await env.DB.prepare('SELECT count FROM pool_quota WHERE install = ?1 AND day = ?2').bind(install, day).first())?.count ?? 0;
  const room = Math.min(lines.length, DAILY_LIMIT - used);
  if (room <= 0) return json({ error: 'quota' }, 429, { 'retry-after': String(untilMidnight(now)) });
  if (lines.length === 0) return json({ accepted: 0, stored: 0 });
  await env.DB.prepare('INSERT INTO pool_quota(install, day, count) VALUES (?1, ?2, ?3) ' +
    'ON CONFLICT(install, day) DO UPDATE SET count = count + excluded.count').bind(install, day, room).run();

  const entries = await prepare(lines.slice(0, room), now);
  const { game, title } = gameOf(body.game);
  let stored = 0;
  let fresh = 0;
  if (entries.length > 0) {
    const translations = entries.flatMap((e) => [...e.translations].map(([engine, tr]) => ({ k: e.k, e: engine, t: tr })));
    const statements = [
      env.DB.prepare(SEEN_SQL).bind(game, install, now, JSON.stringify(entries.map((e) => e.k))),
      env.DB.prepare(LINES_SQL).bind(game, JSON.stringify(entries.map(({ k, en, at, n }) => ({ k, en, at, n })))),
      env.DB.prepare(GAMES_SQL).bind(game, title, now),
    ];
    if (translations.length) statements.push(env.DB.prepare(TRANSLATIONS_SQL).bind(game, JSON.stringify(translations), now));
    const results = await env.DB.batch(statements);
    fresh = results[0]?.meta?.changes ?? 0;
    stored = entries.length;
  }
  // TODO(paketler): Paket üretimi burada değil, ayrı bir zamanlanmış işte olacak (README.md "Paketler nasıl çıkacak"):
  // installs >= 2 satırlar seçilir, çevrilir, gözden geçirilir ve imzalı paket olarak uygulamaya yayınlanır. Şimdilik
  // yalnız toplanıyor; burada hiçbir çeviri ya da yapay zekâ çağrısı yapılmaz.
  console.log(JSON.stringify({ lines: 'submit', game, received: body.lines.length, room, accepted: entries.length, fresh }));
  return json({ accepted: entries.length, stored, fresh });
}

/** Saklama: kurulum–satır eşleşmeleri 180 günden, günlük sınır sayaçları 7 günden eski olunca silinir. */
export async function pruneLines(env, now = Math.floor(Date.now() / 1000)) {
  const results = await env.DB.batch([
    env.DB.prepare('DELETE FROM pool_seen WHERE at < ?1').bind(now - RETENTION_DAYS * 86400),
    env.DB.prepare('DELETE FROM pool_quota WHERE day < ?1').bind(utcDay(now - QUOTA_KEEP_DAYS * 86400)),
  ]);
  return { seen: results[0]?.meta?.changes ?? 0, quota: results[1]?.meta?.changes ?? 0 };
}

// --- Yönetim (Basic auth index.js'te) ---

export async function gamesSummary(env) {
  const now = Math.floor(Date.now() / 1000);
  const week = now - 7 * 86400;
  const all = (sql, ...args) => env.DB.prepare(sql).bind(...args).all().then((r) => r.results);
  const [games, contributors, totals, installs] = await Promise.all([
    all(`SELECT g.game, g.title, g.first_seen, g.last_seen, COUNT(l.key) AS lines, COALESCE(SUM(l.installs >= 2), 0) AS confirmed,
         COALESCE(SUM(l.last_seen >= ?1), 0) AS recent, COALESCE(SUM(l.first_seen >= ?1), 0) AS fresh
         FROM pool_games g LEFT JOIN pool_lines l ON l.game = g.game GROUP BY g.game ORDER BY lines DESC, g.last_seen DESC`, week),
    all('SELECT game, COUNT(DISTINCT install) AS n FROM pool_seen GROUP BY game'),
    env.DB.prepare(`SELECT COUNT(*) AS lines, COALESCE(SUM(installs >= 2), 0) AS confirmed, COALESCE(SUM(last_seen >= ?1), 0) AS recent,
                    COALESCE(SUM(first_seen >= ?1), 0) AS fresh, COALESCE(SUM(sightings), 0) AS sightings FROM pool_lines`).bind(week).first(),
    env.DB.prepare('SELECT COUNT(DISTINCT install) AS n FROM pool_seen').first(),
  ]);
  const byGame = new Map(contributors.map((r) => [r.game, r.n]));
  return {
    at: now,
    totals: { ...totals, installs: installs?.n ?? 0, games: games.length },
    games: games.map((g) => ({ ...g, installs: byGame.get(g.game) ?? 0 })),
  };
}

const likeEscape = (q) => `%${q.replace(/[\\%_]/g, '\\$&')}%`;

export async function linesList(url, env) {
  const game = url.searchParams.get('game') || '';
  if (!GAME_ID.test(game)) return json({ error: 'game gerekli' }, 400);
  const q = (url.searchParams.get('q') || '').trim().slice(0, 100);
  const limit = Math.min(500, Math.max(1, Number.parseInt(url.searchParams.get('limit') || '100', 10) || 100));
  const offset = Math.max(0, Number.parseInt(url.searchParams.get('offset') || '0', 10) || 0);
  const where = q ? "game = ?1 AND en LIKE ?2 ESCAPE '\\'" : 'game = ?1';
  const args = q ? [game, likeEscape(q)] : [game];
  const [rows, count, engines] = await Promise.all([
    env.DB.prepare(`SELECT key, en, installs, sightings, first_seen, last_seen FROM pool_lines WHERE ${where}
                    ORDER BY installs DESC, sightings DESC, last_seen DESC LIMIT ${limit} OFFSET ${offset}`).bind(...args).all().then((r) => r.results),
    env.DB.prepare(`SELECT COUNT(*) AS n FROM pool_lines WHERE ${where}`).bind(...args).first(),
    env.DB.prepare('SELECT DISTINCT engine FROM pool_translations WHERE game = ?1 ORDER BY engine').bind(game).all().then((r) => r.results.map((e) => e.engine)),
  ]);
  const translations = rows.length ? (await env.DB.prepare(
    'SELECT key, engine, tr FROM pool_translations WHERE game = ?1 AND key IN (SELECT value FROM json_each(?2))'
  ).bind(game, JSON.stringify(rows.map((r) => r.key))).all()).results : [];
  const byKey = new Map();
  for (const t of translations) {
    if (!byKey.has(t.key)) byKey.set(t.key, {});
    byKey.get(t.key)[t.engine] = t.tr;
  }
  return json({ game, total: count?.n ?? 0, limit, offset, engines,
    lines: rows.map((r) => ({ ...r, translations: byKey.get(r.key) ?? {} })) }, 200, { 'cache-control': 'no-store' });
}

/** CSV hücresi: tırnaklı; Excel'de formül sayılmasın diye = + - @ ile başlayan hücreye ' eklenir. */
export function csvCell(value) {
  let text = value === null || value === undefined ? '' : String(value);
  if (/^[=+\-@\t\r]/.test(text)) text = `'${text}`;
  return `"${text.replace(/"/g, '""')}"`;
}

const isoDate = (unix) => (unix ? new Date(unix * 1000).toISOString().slice(0, 19).replace('T', ' ') : '');

export async function linesCsv(url, env) {
  const game = url.searchParams.get('game') || '';
  if (!GAME_ID.test(game)) return json({ error: 'game gerekli' }, 400);
  const [rows, translations] = await Promise.all([
    env.DB.prepare(`SELECT key, en, installs, sightings, first_seen, last_seen FROM pool_lines WHERE game = ?1
                    ORDER BY installs DESC, sightings DESC LIMIT 50000`).bind(game).all().then((r) => r.results),
    env.DB.prepare('SELECT key, engine, tr FROM pool_translations WHERE game = ?1').bind(game).all().then((r) => r.results),
  ]);
  const engines = [...new Set(translations.map((t) => t.engine))].sort();
  const byKey = new Map();
  for (const t of translations) {
    if (!byKey.has(t.key)) byKey.set(t.key, {});
    byKey.get(t.key)[t.engine] = t.tr;
  }
  const header = ['en', 'installs', 'sightings', 'first_seen', 'last_seen', ...engines];
  const out = [header.map(csvCell).join(',')];
  for (const r of rows) {
    const tr = byKey.get(r.key) ?? {};
    out.push([r.en, r.installs, r.sightings, isoDate(r.first_seen), isoDate(r.last_seen), ...engines.map((e) => tr[e] ?? '')].map(csvCell).join(','));
  }
  const name = `cevirgen-satirlar-${game}-${new Date().toISOString().slice(0, 10)}.csv`;
  return new Response('﻿' + out.join('\r\n') + '\r\n', {
    headers: { 'content-type': 'text/csv; charset=utf-8', 'content-disposition': `attachment; filename="${name}"`, 'cache-control': 'no-store' },
  });
}

// --- D1 hataları ---

const D1_LIMIT = /exceed|limit|quota/i;
const D1_TRANSIENT = /SQLITE_BUSY|overloaded|unavailable|timed? ?out|reset|network connection lost|internal error|no such table/i;

/** D1 hatası geçiciyse Retry-After saniyesi, değilse 0 (CouchSubs havuzundaki unavailableFor gibi). */
export function unavailableFor(error) {
  const message = String(error?.message || error);
  if (D1_LIMIT.test(message)) return Math.min(3600, untilMidnight(Math.floor(Date.now() / 1000)));
  return D1_TRANSIENT.test(message) ? 60 : 0;
}
