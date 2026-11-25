// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;
using Cevirgen.RemotePlay;

namespace Cevirgen.Shell;

/// <summary>Çevirgen: Canlı ekranın bildirim şeridi için durum (sıra ve önem <see cref="LiveNotices"/>'ta).</summary>
public sealed partial class AppController
{
    /// <summary>
    /// TV bildirimi gerekli mi: altyazı TV'ye gidiyor ve TV eşleşmedi, uygulaması eski ya da güncelleniyor, son denemede TV
    /// uygulaması bağlanmadı, ya da çeviri sürerken TV uygulaması bağlı değil. Çeviri durmuşken bağlı olmayan TV uygulaması
    /// beklenen hâldir (çeviri başlayınca açılır), bildirim çıkmaz.
    /// </summary>
    public bool TvNeedsAttention => ShowsOnTv && (NeedsTv || TvAppUpdating || ShowTvAppUpdate || ShowTestTvHint
                                                  || isRunning && !demoMode && HasTv && tvClients == 0);

    /// <summary>Şu anki bildirim durumu; <paramref name="crashPending"/> arayüzün kilitlenme kaydı izleyicisinden gelir.</summary>
    public LiveNoticeState NoticeState(bool crashPending)
    {
        var now = Clock();
        return new LiveNoticeState
        {
            Running = isRunning,
            DemoMode = demoMode,
            CanStart = CanStart,
            EngineFailed = translatorStatus == TranslatorStatus.EngineFailed,
            ModelMissing = !ModelInstalled,
            FreeEngine = settings.Engine == TranslationEngine.Free,
            OcrMissing = OcrMissing,
            ConsoleNeedsLink = NeedsXboxLink,
            ConsoleOffersReconnect = OffersReconnect,
            Problem = HomeProblem.For(isRunning, demoMode, LastFrameAt is { } at ? now - at : null, ScreenMenuOpen, ShowsSubtitleOffTip),
            TvNeedsWork = TvNeedsAttention,
            Firewall = FirewallPrompt,
            OnlineIssue = OnlineIssue is not null,
            AreaProfileOffered = OfferedAreaProfileGame is not null,
            AreaProfileActive = AreaProfileGame is not null,
            AskingGame = AskingGame,
            CrashPending = crashPending,
            GpuFallback = GpuFallbackWarning,
            MainAccountTip = ShowsMainAccountTip,
            RemotePlayPopup = ShowsRemotePlayHint,
            AutoScreenTextNotice = ShowsAutoScreenTextNotice,
            WindowMissing = CaptureWindowMissing,
            ReadsWindow = ReadsCaptureWindow || ReadsRemotePlayWindow,
        };
    }

    /// <summary>
    /// Çevirgen: çeviri sürüyor, görüntü bir pencereden okunuyor (Bilgisayar, Diğer) ve o pencere bulunamıyor: kapalı ya da
    /// simge durumunda (tam ekran oyun Çevirgen'e geçince küçülür). Canlı ekran uyarır, buradan başka pencere seçilir.
    /// </summary>
    public bool CaptureWindowMissing => isRunning && !demoMode && ReadsCaptureWindow && settings.CaptureWindow is not null
                                        && sessionState.Phase == SessionPhase.Connecting;

    /// <summary>Okunan pencerenin adı (bildirim metni için); seçilmediyse boş.</summary>
    public string CaptureWindowTitle => settings.CaptureWindow?.Title ?? "";

    /// <summary>Canlı ekrandan pencere seçimi: okunan pencere değişir, çeviri sürüyorsa yeni pencereye geçilir.</summary>
    public void ChooseCaptureWindow(WindowCaptureTarget target) => Settings = settings with { CaptureWindow = target };

    /// <summary>Görünen bildirimler, en önemlisi başta (<see cref="LiveNotices.For"/>).</summary>
    public IReadOnlyList<LiveNoticeKind> Notices(bool crashPending) => LiveNotices.For(NoticeState(crashPending));
}
