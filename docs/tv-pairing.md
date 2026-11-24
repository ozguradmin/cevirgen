# TV eşleştirme ve altyazı adresi

TV uygulaması, bilgisayardaki Çevirgen ile aynı yerel ağda çalışır.
Çeviri oturumunu başlatın. LG/Samsung başlatma yolu erişim anahtarını uygulamaya
ulaştırabiliyorsa ek işlem gerekmez. Android TV veya yalnız ağ taramasıyla bağlanan
TV, iki ekranda aynı altı haneli kodu gösterir. **Kodu karşılaştırıp bilgisayardan
onaylayın.** Kumandayla kod girilmez. Tanımadığınız isteği reddedin.
Kod gelmeden TV "kod isteniyor" yazar; istek art arda yanıtsız kalırsa (TV uygulaması 0.9.81'den beri) ne
yapılacağını ve köşeli parantezde nedenini (`pair 0`: yanıt yok, `hello 0`: Çevirgen'e ulaşılamıyor) gösterir.

Eşleştirme isteği iki dakika geçerlidir; aynı anda bir istek bekler. TV'de yeniden
denenen istek (Tamam'a basmak, uygulamayı yeniden açmak) iki saniyeden eski onaysız
isteğin yerini alır; onay penceresi yeni kodla açılır. Ret sonrasında ve süre dolduğu
andan itibaren on saniyelik bekleme vardır. Onay bekleyen istek, sunucu yeniden açılsa
ya da port değişse de korunur. Süresi dolan ya da bilgisayarda düşen istek
(çeviri durdurulup başlatıldı, uygulama yeniden açıldı) TV uygulaması 0.9.62'den beri
kendiliğinden yeniden gönderilir ve onay penceresi yeniden açılır; yalnız reddedilen
istekten sonra TV Tamam bekler (sunucu `{"state":"expired","rejected":true}` döner). Ayarlardaki “TV eşleştir”, çeviri çalışmadan da
iki dakikalık eşleştirme penceresi açar.
Android TV'de eşleştirme için sürekli ADB veya geliştirici bağlantısı gerekmez.

## OBS ve başka cihazın tarayıcısı

Ayarlardan altyazı/OBS adresini kopyalayın. Adres `?k=…` erişim anahtarı içerir;
anahtarı günlük, ekran görüntüsü veya açık yayında paylaşmayın. Bilgisayardaki OBS
`localhost` adresini, başka cihaz bilgisayarın ağ adresini kullanır.
`/obs` sayfasının kendisi açık olsa da altyazı verisini anahtarlı `/events` üzerinden alır.

“Erişim anahtarını yenile” mevcut altyazı bağlantılarını kapatır. OBS adresini yeniden
kopyalayın; TV başlatma parametresiyle yeni anahtarı alır veya yeniden onay ister.
Sunucu kimliği anahtardan türetilir: anahtar değişmeden IP değişirse TV eski onayını
kullanabilir, anahtar yenilenince eski kimlik geçersiz olur.

## Geçiş sürümünün sınırları

- Yeni TV, `auth: 1` bildirmeyen eski sunucuya anahtarsız bağlanabilir.
- Yeni sunucu **yalnız bir geçiş sürümü boyunca**, kökensiz veya `null`/paketli
  uygulama kökenli eski istemcileri anahtarsız kabul eder ve arayüzde güncelleme
  uyarısı gösterir. Yanlış verilmiş anahtar yine reddedilir. Sonraki sürümde
  `AllowLegacyClients` / `allowLegacyClients` / `ALLOW_LEGACY_CLIENTS` kapatılmalıdır.
- Bu geçiş, yetkisiz yerel istemciyi veya sandbox iframe ile oluşturulan
  `Origin: null` erişimini tamamen engellemez. Yabancı HTTP(S) kökenleri reddedilir;
  CORS tek başına kimlik doğrulama değildir.
- Bağlantılar yerel/özel adreslerle sınırlıdır; bu, genel bir kimlik veya güvenilir
  Wi-Fi garantisi değildir. Anahtar HTTP üzerinden taşınır: bu değişiklik TLS veya
  uçtan uca şifreleme sağlamaz, ağdaki dinleme/aktif araya girme saldırılarını çözmez.
- Karşılaştırma kodu anahtar değildir. Onaylanan anahtar yalnız TV'ye verilen
  tahmin edilemez istek sırrıyla ve isteğin ömrü boyunca alınabilir.

Geliştirici kontrolü: ortak `subtitle-access.json` ve `subtitle-pairing.json`
vektörleri (`shared/fixtures`) Windows testlerinde; eski/yeni TV bağlantısı ve anahtar
geçişleri `node --test scripts/tv-discovery.test.js` içinde sınanır. TV sayfası gerçek ağ üzerinden
`scripts/tv-pair-live.js` ile çalışan bir sunucuya eşleştirilebilir. Bunlar canlı TV, OBS ve üretim paketi
testlerinin yerini tutmaz.
