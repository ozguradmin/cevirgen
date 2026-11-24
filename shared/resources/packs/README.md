# Uygulamayla gelen oyun paketleri

Paket dosyaları (`*.json.gz`) depoda değildir. Yayın derlemesinden önce bu klasöre konursa Windows uygulaması onları
gömülü kaynak olarak içerir (`windows/src/Cevirgen.Core/Cevirgen.Core.csproj`, ad: `packs/<ad>.json.gz`). Özel bir R2
kovasından indirmek için:

```sh
python3 scripts/fetch-packs.py            # erişim: PACKS_R2_* ortam değişkenleri
```

Klasör boşsa uygulama paketsiz derlenir; gerçek paketlere dayanan testler kendini atlar.
