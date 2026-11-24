-- SPDX-License-Identifier: AGPL-3.0-only
-- Çevirgen anonim kullanım sayımı. Kişisel veri yok: IP, ad, oyun içeriği, altyazı saklanmaz.
-- id: uygulamanın ilk açılışta ürettiği rastgele UUID (kullanıcı ayarlardan kapatabilir, silince yenisi üretilir).

CREATE TABLE IF NOT EXISTS installs (
  id TEXT PRIMARY KEY,
  first_seen INTEGER NOT NULL,   -- unix saniye
  last_seen INTEGER NOT NULL,
  version TEXT NOT NULL,
  platform TEXT NOT NULL,        -- ps5 | xbox | pc | capture | unknown
  playing INTEGER NOT NULL DEFAULT 0  -- son sinyalde çeviri açık mıydı
);
CREATE INDEX IF NOT EXISTS installs_last_seen ON installs(last_seen);

-- Gün başına tekil kurulum (günlük aktif sayısı ve 30 günlük grafik için).
CREATE TABLE IF NOT EXISTS days (
  day TEXT NOT NULL,             -- YYYY-MM-DD (UTC+3, Türkiye)
  id TEXT NOT NULL,
  played INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (day, id)
);

-- ───── Ortak havuz (2026-11-24; migrate-2026-11-24-lines.sql ile aynı) ─────

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
