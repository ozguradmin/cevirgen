-- SPDX-License-Identifier: AGPL-3.0-only
-- Çevirgen ortak havuzu (yalnız toplama; POST /v1/lines, src/lines.js). Uygula:
--   wrangler d1 execute cevirgen-api --remote --file=migrate-2026-11-24-lines.sql
-- Kurulum kimliği hiçbir tabloda düz durmaz: tuzlu SHA-256'nın ilk 8 baytı (16 onaltılık). IP saklanmaz.

-- Oyun: uygulamanın oyun/paket kimliği ("gta-6") ya da adının anahtarı; bilinmiyorsa "unknown". Ad en son gelen.
CREATE TABLE IF NOT EXISTS pool_games (
  game TEXT PRIMARY KEY,
  title TEXT,
  first_seen INTEGER NOT NULL,   -- unix saniye
  last_seen INTEGER NOT NULL
);

-- Oyun + İngilizce anahtarı (canon + SHA-256 ilk 8 bayt) başına bir satır. installs: satırı gönderen tekil kurulum sayısı
-- (pool_seen'den; eski görülmeler silinse de düşmez), sightings: toplam görülme.
CREATE TABLE IF NOT EXISTS pool_lines (
  game TEXT NOT NULL,
  key TEXT NOT NULL,
  en TEXT NOT NULL,
  first_seen INTEGER NOT NULL,
  last_seen INTEGER NOT NULL,
  installs INTEGER NOT NULL DEFAULT 0,
  sightings INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (game, key)
);
CREATE INDEX IF NOT EXISTS pool_lines_top ON pool_lines(game, installs DESC, sightings DESC);
CREATE INDEX IF NOT EXISTS pool_lines_last_seen ON pool_lines(last_seen);

-- Hangi kurulum hangi satırı gördü (tekil sayım için); 180 günden eskiler gece silinir.
CREATE TABLE IF NOT EXISTS pool_seen (
  game TEXT NOT NULL,
  key TEXT NOT NULL,
  install TEXT NOT NULL,         -- tuzlu özet, kimliğin kendisi değil
  at INTEGER NOT NULL,           -- bu kurulumun ilk gönderdiği an
  PRIMARY KEY (game, key, install)
);
CREATE INDEX IF NOT EXISTS pool_seen_at ON pool_seen(at);
CREATE INDEX IF NOT EXISTS pool_seen_install ON pool_seen(game, install);

-- Motor başına en son çeviri ("free", "deepl", "gemini:<model>", …, kullanıcının düzeltmesi "user").
CREATE TABLE IF NOT EXISTS pool_translations (
  game TEXT NOT NULL,
  key TEXT NOT NULL,
  engine TEXT NOT NULL,
  tr TEXT NOT NULL,
  at INTEGER NOT NULL,
  PRIMARY KEY (game, key, engine)
);

-- Kurulum başına günlük satır sayacı (UTC gün); 7 günden eskiler gece silinir.
CREATE TABLE IF NOT EXISTS pool_quota (
  install TEXT NOT NULL,         -- tuzlu özet
  day TEXT NOT NULL,             -- YYYY-MM-DD (UTC)
  count INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (install, day)
);
