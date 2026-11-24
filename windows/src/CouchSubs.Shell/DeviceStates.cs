// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.RemotePlay;

namespace CouchSubs.Shell;

/// <summary>
/// Çevirgen: bir cihazın ya da çeviri motorunun durumu; her yerde aynı renkle gösterilir (üst çubuk, Canlı ekranın boş hâli,
/// Ayarlar › Cihazlar, Çeviri › motor listesi).
/// <list type="bullet">
/// <item><see cref="Ready"/> yeşil: bağlı ya da kullanıma hazır (çeviri başlayınca kendiliğinden bağlanır).</item>
/// <item><see cref="Waiting"/> sarı-turuncu: bekleniyor, bağlanıyor ya da kullanıcının bir şey yapması gerekiyor.</item>
/// <item><see cref="Error"/> kırmızı: hata, ulaşılamıyor; kendiliğinden düzelmeyecek.</item>
/// <item><see cref="Off"/> gri: kapalı ya da kurulmamış.</item>
/// </list>
/// </summary>
public enum DeviceState { Off, Waiting, Ready, Error }

/// <summary>Durumların hesabı (arayüzden bağımsız; sınanır).</summary>
public static class DeviceStates
{
    /// <summary>
    /// Görüntü kaynağı (PS5, Xbox, seçilen pencere). Kurulmamışsa kapalı; çeviri durmuşken kurulu kaynak hazırdır (başlayınca
    /// bağlanır). Çeviri sürerken: görüntü geliyorsa hazır, bağlanıyorsa ya da yeniden deneniyorsa bekleniyor; yeniden deneme
    /// bıraktıysa (kullanıcının "Yeniden bağlan"a basması gerekir) hata.
    /// </summary>
    public static DeviceState Source(bool demo, bool configured, bool running, SessionPhase phase, bool retryStopped)
    {
        if (demo) return DeviceState.Ready;
        if (!configured) return DeviceState.Off;
        if (!running) return DeviceState.Ready;
        return phase switch
        {
            SessionPhase.Streaming => DeviceState.Ready,
            SessionPhase.Connecting => DeviceState.Waiting,
            _ => retryStopped ? DeviceState.Error : DeviceState.Waiting,
        };
    }

    /// <summary>
    /// Altyazının gittiği yer. Yalnız bu bilgisayar seçiliyse hazır. TV eşleşmemişse kapalı. TV uygulaması eskiyse
    /// güncellenmeyi bekler. TV uygulaması bağlıysa hazır; çeviri sürerken bağlı değilse bekleniyor (TV uygulaması açılıyor ya da
    /// yeniden bağlanılıyor); çeviri durmuşken eşleşmiş TV hazırdır (çeviri başlayınca açılır).
    /// </summary>
    public static DeviceState Tv(bool showsOnTv, bool hasTv, bool running, int clients, bool appOutdated)
    {
        if (!showsOnTv) return DeviceState.Ready;
        if (!hasTv) return DeviceState.Off;
        if (appOutdated) return DeviceState.Waiting;
        if (clients > 0) return DeviceState.Ready;
        return running ? DeviceState.Waiting : DeviceState.Ready;
    }

    /// <summary>
    /// Çeviri motoru. Açılamadıysa ya da çevrimiçi motor şu an çeviremiyorsa hata; ücretsiz modelin indirilmesi gerekiyorsa
    /// ya da seçili motorun anahtarı eksikse bekleniyor (kullanıcı bir şey yapmalı); değilse hazır.
    /// </summary>
    public static DeviceState Engine(bool failed, bool needsDownload, bool configured) =>
        failed ? DeviceState.Error : needsDownload || !configured ? DeviceState.Waiting : DeviceState.Ready;

    /// <summary>Çeviri sekmesindeki listede bir motor: seçili değilse ve kurulmamışsa kapalı (gri).</summary>
    public static DeviceState EngineChoice(bool configured, bool needsDownload) =>
        needsDownload ? DeviceState.Waiting : configured ? DeviceState.Ready : DeviceState.Off;

    /// <summary>Kısa Türkçe adı (ekran okuyucu ve ipucu için): "hazır", "bekleniyor", "hata", "kapalı".</summary>
    public static string Name(this DeviceState state) => state switch
    {
        DeviceState.Ready => "hazır",
        DeviceState.Waiting => "bekleniyor",
        DeviceState.Error => "hata",
        _ => "kapalı",
    };
}
