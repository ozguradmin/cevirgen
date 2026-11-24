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
