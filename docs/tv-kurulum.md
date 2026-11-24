# Çevirgen TV uygulamasını kurma

Çevirgen'in TV uygulaması **Android TV / Google TV** içindir ve mağazada değildir. TV'nizde hata ayıklamayı açmanız
yeter: uygulamayı bilgisayardaki Çevirgen ağ üzerinden kendisi kurar. Hata ayıklama ücretsizdir ve TV'ye zarar vermez.
Adımları bilgisayar uygulaması da TV adımında gösterir ve TV'yi kendisi bekler; bu sayfa aynı adımların ayrıntılı
hâlidir.

| TV | Durum | Rehber |
|---|---|---|
| **Android TV / Google TV** (Vestel, Arçelik, Beko, Grundig, Philips, Sony, TCL ve diğerleri) | Gerçek TV'de denendi. Çevirgen ağ üzerinden kendisi kurar | [Android TV](#android-tv--google-tv) |
| **LG** (webOS), **Samsung** (Tizen) | Çevirgen sürümleri bu TV'lerin paketini içermez. Altyazıyı bilgisayar ekranında ya da Android TV kutusunda gösterebilirsiniz | |
| **Vestel, Arçelik, Beko, Grundig'in Linux modelleri, Philips Saphi ve Titan OS** | Desteklenmiyor: bu TV'lere uygulama kurmanın bir yolu yok | |

**TV'm Android mi?** Ana ekranda Google Play Store ya da Google TV yazıyorsa Android'dir. Üreticinin kendi uygulama
mağazası varsa ve Play Store yoksa büyük olasılıkla değildir.

Bilgisayar ve TV aynı ev ağında olmalıdır.

**Windows güvenlik duvarı:** Ayarlar › Windows Güvenlik Duvarı izni, yalnız Çevirgen uygulaması için
**Özel ağdaki yerel cihazlardan TCP ve UDP bağlantılarını** açar. TV sunucusu 8787 doluysa başka bir port
seçebildiğinden ve PS5 keşfi/yayını değişken portlar kullanabildiğinden izin tek porta bağlanmaz.
Uygulama ağ profilini kendiliğinden değiştirmez; yalnız güvendiğiniz ev ağını Windows Ayarları'nda **Özel** yapın.
Ortak ve Etki alanı profilleri için bu düğme izin açmaz. Ayrı alt ağ/VLAN veya VPN üzerinden bağlantı bu otomatik
kuralın kapsamı dışındadır; gerekirse ağ yöneticisi yalnız ilgili cihazlara özel izin düzenlemelidir.
Önceki sürümün oluşturduğu geniş kural yeni izinler doğrulandıktan sonra taşınır. Yalnız Çevirgen'i hedefleyen engel
kuralları (ör. Windows'un ilk açılış sorusu kapatılınca yazdığı kural) kaldırılır; genel, başka programların ve kurumun
engelleme kuralları otomatik silinmez. İzin doğrulanamıyorsa mevcut engelleme kurallarını Windows Güvenlik Duvarı'nda
kontrol edin; güvenlik duvarını bütünüyle kapatmayın.

**Güncelleme:** Çevirgen uygulama açılınca ve TV her bağlandığında TV'deki uygulamanın sürümüne bakar; bilgisayardaki
paket daha yeniyse ana ekranda **TV uygulamasını güncelle** çıkar. TV uygulaması sürümünü altyazı bağlantısında da
bildirir (`/events?v=`).

## Android TV / Google TV

Gerçek TV'de denendi. Bazı TV'ler HDMI görüntüsünü başka uygulamaya vermeyebilir; böyle bir durumda altyazılar
görünür ama arkası siyah kalır. Lütfen [Issues](https://github.com/ozguradmin/cevirgen/issues) sayfasından
TV modelinizle birlikte yazın.

### 1. TV'de hata ayıklamayı açın

1. **Ayarlar › Cihaz Tercihleri › Hakkında**'ya girin (Google TV'de **Ayarlar › Sistem › Hakkında**).
2. **Yapı numarası**'nın üstünde Tamam tuşuna 7 kez basın. "Geliştiricisiniz" uyarısı çıkar.
3. **Cihaz Tercihleri** (Google TV'de **Sistem**) **› Geliştirici seçenekleri**'nden **USB hata ayıklama**'yı açın.
   **Ağ hata ayıklama** seçeneği varsa onu da açın.

Menü adları markaya göre biraz değişebilir.

### 2. Çevirgen kursun

1. Çevirgen'in TV adımında **Android TV / Google TV**'yi seçin. Çevirgen TV'yi ağda arar.
2. TV ekranında "USB hata ayıklamaya izin verilsin mi?" sorusu çıkar. **Bu bilgisayardan her zaman izin ver**'i
   işaretleyip **İzin ver**'e basın.
3. Çevirgen uygulamayı kurar ve açar. TV uygulaması bilgisayarı ağda kendisi bulur.
4. Görüntü gelmezse Tamam ile paneli açıp sol ve sağ tuşlarıyla doğru HDMI girişini seçin.

Kurulumdan sonra hata ayıklamayı kapatabilirsiniz; yeni sürümleri kurmak için Çevirgen onu yeniden açmanızı ister.

### Görüntü modu: Otomatik, Uygulama içinde, Katman

Varsayılan **Otomatik**'tir: bilgisayarın kurduğu TV uygulaması **Katman**'da açılır; TV, PS5'i kendi HDMI girişinde
(kendi görüntü ayarları, oyun modu ve HDR ile) gösterir, altyazı görüntünün üstünde çıkar. Bazı TV'lerde (ör. Philips
55PUS8609, TCL) uygulamanın içindeki görüntü siyah kalıyor; TV "görüntü geldi" dediği için uygulama bunu fark
edemiyordu, altyazı yalnız Katman seçilince geliyordu.

PS5'i Çevirgen'in içinde görmek isterseniz Ayarlar'daki **Görüntü modu**'ndan **Uygulama içinde**'yi seçin. Bazı
TV'ler (ör. Sony Bravia) bu görüntüyü HDMI girişi olarak değil uygulama olarak işler: uygulama açıkken görüntü modu
değişebilir, oyun modu ya da HDR kapanabilir; o zaman Otomatik'e ya da Katman'a dönün.

### Altyazı var ama oyunun sesi yok

Çeviri başlayınca iki şey aynı anda olur: Çevirgen ikinci hesapla PS5'e Uzaktan Oynatma ile bağlanır, TV uygulaması
da (Uygulama içinde modunda) PS5 görüntüsünü kendi içinde açar. Ses bu ikisinden hangisinde gidiyor, şu denemeyle
ayrılır:

1. Altyazı hedefi olarak TV'yi değil bilgisayar ekranını seçin; TV uygulaması açılmasın.
2. Çeviriyi başlatın, TV'yi kumandanın giriş tuşuyla PS5'in HDMI girişine alın.
3. Ses geliyorsa PS5 sesi kesmiyor; sorun TV uygulamasının içindeki HDMI ses yolunda: Görüntü modunu **Katman** yapın,
   TV'nin dijital ses çıkışını **Otomatik** ya da **PCM** (Geçiş/Pass Through değil), PS5'te **Ayarlar › Ses › Ses
   Çıkışı › Ses Biçimi (Öncelik)**'yi **Linear PCM** yapın. Ses burada da yoksa altyazı günlüğüyle, TV ve ses sistemi
   modeliyle GitHub'da bildirin.

Çevirgen Uzaktan Oynatma'dan gelen sesi istemcide atar (`CHIAKI_AUDIO_DISABLED`); PS5'e ses için bir istek ya da
bayrak gönderilmez. Xbox başka: uzaktan oynatma sürerken TV'nin sesini kendisi kapatır ve sesi yalnız bağlanan cihaza
verir.

### USB bellekle kurulum

Çevirgen TV'yi ağda bulamazsa (bazı TV'ler ağdan hata ayıklamaya izin vermez):

1. **Ayarlar › Uygulamalar › Güvenlik ve kısıtlamalar › Bilinmeyen kaynaklar**'dan dosya yöneticisine izin verin.
2. Bilgisayarda Çevirgen'in kurulu olduğu klasördeki TV paketini (`%LOCALAPPDATA%\CevirgenApp\current\tv-app\` altındaki
   `cevirgen-androidtv_<sürüm>.apk`) bir USB belleğe kopyalayıp TV'ye takın.
3. TV'de dosya yöneticisiyle dosyayı açıp kurun. TV'nizde dosya yöneticisi yoksa Play Store'dan ücretsiz birini kurun.
4. Çevirgen'in TV adımında **Android TV / Google TV**'yi seçin ve TV'de Çevirgen'i açın.
