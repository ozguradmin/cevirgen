# Çevirgen API

Çevirgen'in kendi Cloudflare Worker'ı (`src/index.js`): uzaktan ayarlar, anonim kullanım sayımı, ortak havuz (satır
toplama) ve yönetim paneli. Adres `https://cevirgen.ozgurguler.tech`, yedeği
`https://cevirgen-api.ozgurglr256.workers.dev`. Veritabanı D1 `cevirgen-api` (bağ `DB`), ayarlar KV (`CONFIG`).

## Uçlar

| Uç | Ne yapar |
| --- | --- |
| `GET /v1/config` | Uzaktan ayarlar: reklamlar, açılır reklam, duyuru, oyun kapakları, destek alanı (KV `config`). |
| `GET /v1/assets/<ad>` | Ayarlarda kullanılan görseller (KV `asset:<ad>`). |
| `POST /v1/ping` | Anonim sayım `{id, v, p, playing}`; tablolar `installs`, `days`. |
| `POST /v1/lines` | Ortak havuz, yalnız toplama (aşağıda). |
| `GET /admin` | Yönetim paneli (Basic auth, şifre `ADMIN_TOKEN` secret'ı): analiz verileri, Satırlar, uzaktan ayarlar. |
| `GET /admin/stats.json` | Kullanım istatistikleri. `GET/POST /admin/config`, `POST /admin/asset`: ayarlar ve görsel yükleme. |
| `GET /admin/lines/games.json` | Oyun başına satır, ≥2 kurulumun gördüğü satır, katkı veren kurulum, son 7 gün; genel toplamlar. |
| `GET /admin/lines.json?game=&q=&limit=&offset=` | Bir oyunun satırları (en çok kurulumun gördüğü başta), motor başına çeviriler, metinde arama. |
| `GET /admin/lines.csv?game=` | Bir oyunun bütün satırları CSV (UTF-8 BOM; her motor bir sütun). |

## Ortak havuz (`POST /v1/lines`)

Uygulama ekranda gösterdiği satırları (altyazı, ek alan, ekran yazısı) ve o satırın çevirisini beş dakikada bir, oyuna
göre gruplayıp gönderir:

```json
{ "id": "<kurulum uuid v4>", "v": "1.5.0", "game": { "id": "gta-6", "title": "Grand Theft Auto VI" },
  "lines": [ { "en": "Where is the money, Jason?", "tr": "Para nerede, Jason?", "engine": "gemini:gemini-2.5-flash",
               "area": "subtitle", "at": "2026-11-24T10:00:00Z", "n": 1 } ] }
```

- `engine`: `free`, `deepl`, `gemini:<model>`, `openai:<model>`, `claude:<model>`, `local:<model>`, `custom:<model>`;
  kullanıcının kendi düzeltmesi `user`. `area`: `subtitle`, `extra`, `screen`. `n`: son gönderimden beri görülme (1–50).
- Oyun anahtarı: geçerli `game.id`, yoksa adın anahtarı (`games.js`: "Grand Theft Auto VI" → `gta-6`), yoksa `unknown`.
- Sınırlar: istek başına 200 satır, gövde en çok 256 KB (413), kurulum başına günde 2000 satır (429 + `Retry-After`,
  UTC gece yarısına kadar). D1 sınırı ya da geçici hata: 503 + `Retry-After`.
- Süzgeç (`src/lines.js`): CouchSubs havuzunun kuralları (`filter.js`, `prompts.js`: en az iki sözcük, en çok 300
  karakter, yarım okuma, Türkçe, PS5 sistem yazısı, tuş ipucu, çoğu simge/rakam olan satır) ve kişisel bilgi süzgeci:
  e-posta, bağlantı/alan adı, 7+ rakamlı telefon benzeri dizi, `@ad`, `#numara`, kullanıcı adı / PSN kimliğine benzeyen
  sözcük (alt çizgili ya da harfle birlikte iki ve daha çok rakam: `xX_Gamer_99`, `Player1234`; `1990s`, `10pm`, `4x4`
  gibi olağan sözcükler hariç) ve PS5'in "Uzaktan Oynatma'ya bağlandı" / "Remote Play" bildirimi. Çeviride kişisel bilgi
  varsa satır yine atılır. Süzgeç tutucudur: şüpheli satır hiç yazılmaz.
- Kurulum kimliği hiçbir tabloda düz durmaz: `SHA-256("<tuz>:<id>")` ilk 8 bayt (tuz `LINES_PEPPER` secret'ı, yoksa
  sabit; değiştirilirse tekil kurulum sayımı sıfırdan başlar). IP saklanmaz. Panel kurulum kimliği/özeti göstermez.

### Tablolar (`migrate-2026-11-24-lines.sql`, `schema.sql`)

- `pool_games(game, title, first_seen, last_seen)`: oyun ve en son gelen adı.
- `pool_lines(game, key, en, first_seen, last_seen, installs, sightings)`: oyun + İngilizce anahtarı (CouchSubs `canon()`
  + SHA-256 ilk 8 bayt; uygulamadaki `PackKey` ile aynı) başına bir satır. `installs` tekil kurulum sayısı (eski
  görülmeler silinse de düşmez), `sightings` toplam görülme.
- `pool_seen(game, key, install, at)`: hangi kurulum özeti hangi satırı gördü (tekil sayım için).
- `pool_translations(game, key, engine, tr, at)`: motor başına en son çeviri (motorları karşılaştırmak için).
- `pool_quota(install, day, count)`: günlük sınır sayacı.

Bir gönderi D1'de en çok ~6 sorgu yapar (satırlar `json_each` ile tek değişkende); aynı çeviri yeniden yazılmaz.

### Saklama

Cron (`wrangler.toml`, her gece 02:23 UTC): 180 günden eski `pool_seen` satırları ve 7 günden eski `pool_quota`
satırları silinir. Toplam sayılar (`pool_lines`) ve çeviriler kalır.

## Paketler nasıl çıkacak (henüz yok)

Şimdilik yalnız toplanıyor; çeviri ya da paket üretimi yapılmıyor (`src/index.js` ve `src/lines.js` içinde
`TODO(paketler)`). Plan, CouchSubs'ın havuz ve paket sunucularına (`server/pool/src/autopack.js`,
`server/packs/src/index.ts`) benzer. Upstream'in `server/pool` ve `server/packs` başvuru uygulaması bu deponun git
geçmişinde (2026-11-24 temizliğinden önceki sürümler) ve upstream depoda (github.com/yorulmazsinan/couchsubs) durur.

1. **Seçim:** oyun başına `installs >= 2` olan (en az iki ayrı kurulumun gördüğü) ve henüz pakette olmayan satırlar. Tek
   kurulumun gördüğü satır pakete girmez (yanlış okuma, kişisel bilgi riski).
2. **Çeviri:** satırlar oyunun adı ve bağlamıyla toplu çevrilir (Claude ya da Workers AI; ya da `pool_translations`'ta
   en iyi sonucu veren motorun çevirisi). `user` çevirileri (kullanıcı düzeltmeleri) güçlü işaret sayılır.
3. **Gözden geçirme:** paket sahibinin onayıyla (panelde ya da CSV üzerinden) yayınlanır.
4. **Yayın:** paket imzalanır (Ed25519, CouchSubs `PackSigning`) ve imzalı paket ucundan (`GET /v1/packs` listesi +
   paket dosyası, upstream `server/packs` gibi) uygulamaya gider; uygulama `PackUpdater` ile indirir, `PackSigning` ile
   doğrular, `GamePacks` ile kullanır. Paket kimliği oyun anahtarıyla aynı (`gta-6`).

## Kurulum ve yayın

```sh
wrangler d1 execute cevirgen-api --remote --file=migrate-2026-11-24-lines.sql   # bir kez
wrangler deploy
```

`ADMIN_TOKEN` (ve isteğe bağlı `GITHUB_TOKEN`) secret'tır: `wrangler secret put ADMIN_TOKEN`.
