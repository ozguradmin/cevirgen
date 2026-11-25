// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Cevirgen.App.Controls;
using Cevirgen.App.Theme;
using Cevirgen.Core;
using Cevirgen.Shell;

namespace Cevirgen.App.Views;

/// <summary>
/// Canlı ekran (taslak: Main.dc.html): TV önizlemesi (son kare + TV'deki Türkçe satırlar), tek satır sayılar, bildirimler ve
/// sağda döküm. Çeviri kapalıyken önizlemenin üstünde "Çeviriyi başlat" ve bağlı olanlar. Saniyede bir sessizlik nedeni
/// ve sayılar yenilenir.
/// </summary>
public partial class MainView : UserControl
{
    private readonly AppController controller;
    private readonly LiveStage stage;
    // Çeviri sürerken PS5 ve TV bağlanamıyorsa güvenlik duvarı kartı önerilir.
    private readonly DispatcherTimer connectionWatch = new() { Interval = TimeSpan.FromSeconds(5) };
    /// <summary>Sessizlik nedeni ve sayılar saniyede bir yenilenir.</summary>
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Border emptyState;
    private readonly StackPanel emptyDevices = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = Sp.T("0", "l", "0", "0") };
    private readonly TextBlock emptyMessage = new()
    {
        Text = "Çeviri başlayınca oyunun görüntüsü burada görünür", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center, Margin = Sp.T("0", "0", "0", "m"), Foreground = PerdeColors.Frozen(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
    };
    private readonly Button startButton = new() { Content = "Çeviriyi başlat", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly GamePickerView picker;

    public MainView(AppController controller)
    {
        this.controller = controller;
        DataContext = controller;
        InitializeComponent();
        stage = new LiveStage(controller);
        stage.Changed += ShowStage;
        emptyState = BuildEmptyState();
        Preview.Overlay = emptyState;
        picker = new GamePickerView(controller) { Margin = Sp.T("xl", "l", "xl", "l") };
        GameAsk.Children.Add(picker);
        GameAsk.SizeChanged += (_, e) => picker.FitTo(e.NewSize.Width, e.NewSize.Height);
        controller.PropertyChanged += OnChanged;
        connectionWatch.Tick += async (_, _) => await controller.CheckConnectionsAsync();
        tick.Tick += (_, _) => OnTick();
        Loaded += (_, _) =>
        {
            stage.Attach();
            connectionWatch.Start();
            tick.Start();
            Preview.SetFrame(controller.LastFrame);
            // Çeviri kapalıyken önceki oturumun son gerçek karesi (yoksa düz yüzey).
            if (!controller.IsRunning) Preview.SetStill(Services.LastFrameStore.Load());
            ShowEmptyState();
            ShowStage();
        };
        Services.CrashWatch.Changed += OnCrashChanged;
        Unloaded += (_, _) =>
        {
            connectionWatch.Stop();
            tick.Stop();
        };
        SizeChanged += (_, _) => FitAside();
        StageArea.SizeChanged += (_, _) => FitPreview();
        StatsRow.SizeChanged += (_, _) => FitPreview();
        stage.Attach();
        Refresh();
        ShowGame();
        ShowFeed();
        ShowStats();
        ShowLook();
        ShowNotices();
        OnTick();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppController.LastFrame):
                if (IsVisible) Preview.SetFrame(controller.LastFrame);
                return;
        }
        if (e.PropertyName is nameof(AppController.IsRunning) or nameof(AppController.TranslatorStatus) or nameof(AppController.ModelInstalled)
            or nameof(AppController.OnlineIssue) or nameof(AppController.CanStart) or nameof(AppController.IsStarting)
            or nameof(AppController.DemoMode) or nameof(AppController.Ps5Tone) or nameof(AppController.TvTone) or nameof(AppController.TvClients)
            or nameof(AppController.Settings) or nameof(AppController.Tv) or nameof(AppController.Console))
            Refresh();
        if (e.PropertyName is nameof(AppController.Settings) or nameof(AppController.SubtitlesHidden))
        {
            ShowLook();
            ShowStage();
        }
        if (e.PropertyName is nameof(AppController.IsRunning) or nameof(AppController.DemoMode) or nameof(AppController.PlayedGame) or nameof(AppController.AskingGame)
            or nameof(AppController.AreaProfileGame) or nameof(AppController.OfferedAreaProfileGame) or nameof(AppController.Settings))
            ShowGame();
        if (e.PropertyName is nameof(AppController.History) or nameof(AppController.LastTranslation) or nameof(AppController.IsRunning)
            or nameof(AppController.SessionStats) or nameof(AppController.SessionStartedAt) or nameof(AppController.GlossaryTerms))
        {
            ShowFeed();
            ShowStats();
        }
        if (e.PropertyName is nameof(AppController.RemoteGames) or nameof(AppController.InstalledPacks) && controller.AskingGame)
            picker.Refresh();
        if (e.PropertyName is nameof(AppController.Support) or nameof(AppController.SupportUnlocked))
            ShowSupport();
        // Şerit yapısı değişmediyse yalnız iletileri yeniler; her durum değişikliğinde sorulur (ucuz).
        if (e.PropertyName is not (nameof(AppController.History) or nameof(AppController.SessionStats) or nameof(AppController.MetricsText)))
            ShowNotices();
    }

    /// <summary>Saniyede bir: bildirimler (görüntü kaç sn'dir yok, sessizlik nedeni) ve sayılar.</summary>
    private void OnTick()
    {
        ShowNotices();
        ShowStats();
    }

    /// <summary>Dar pencerede döküm sütunu daralır (taslak: 380–440).</summary>
    private void FitAside()
    {
        if (ActualWidth <= 0) return;
        AsideColumn.Width = new GridLength(Math.Clamp(ActualWidth * 0.32, 340, 440));
    }

    /// <summary>Önizleme kalan yüksekliğe sığar (16:9 korunur), sayılar satırı hemen altında kalır.</summary>
    private void FitPreview()
    {
        if (StageArea.ActualHeight <= 0) return;
        Preview.MaxHeight = Math.Max(120, StageArea.ActualHeight - StatsRow.ActualHeight - StatsRow.Margin.Top);
    }

    /// <summary>Kabuk bu ana ekranı bırakınca (kurulum yeniden açıldı) denetleyiciden ayrılır.</summary>
    public void Detach()
    {
        controller.PropertyChanged -= OnChanged;
        Services.CrashWatch.Changed -= OnCrashChanged;
        stage.Detach();
        connectionWatch.Stop();
        tick.Stop();
    }

    /// <summary>Ekran görüntüsü aracı: TV'ye gidiyormuş gibi bir altyazı olayı.</summary>
    internal void ShowEventForRender(SubtitleEvent e) => stage.OnEvent(e);

    // ───── TV önizlemesi ─────

    /// <summary>TV'de şu an görünen satırlar önizlemeye ve dökümün "● ekranda" işaretine.</summary>
    private void ShowStage()
    {
        var englishBox = stage.EnglishBox ?? LivePreviewLayout.FromRegion(controller.Settings.Region);
        Preview.SetLines(stage.Lines, controller.Settings.Style, englishBox, controller.SubtitlesHidden);
        Preview.SetRegions(stage.Regions);
        ShowFeed();
    }

    /// <summary>
    /// Çeviri kapalıyken önizlemenin üstünde (taslak: boş hâl): kare yoksa kısa bir cümle, tek sarı "Çeviriyi başlat" ve
    /// cihazların durumu (yeşil hazır, sarı-turuncu bekleniyor, kırmızı hata, gri kapalı). Başlatılamıyorsa ne eksik olduğu
    /// ve oraya giden düğme (çıkmaz yok).
    /// </summary>
    private Border BuildEmptyState()
    {
        startButton.SetResourceReference(StyleProperty, "PrimaryButton");
        startButton.Padding = Sp.T("xl", "0", "xl", "0");
        startButton.Click += async (_, _) => await ShellWindow.ToggleTranslationAsync(controller);
        emptyMessage.SetResourceReference(TextBlock.FontFamilyProperty, "TextFont");
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };
        stack.Children.Add(emptyMessage);
        stack.Children.Add(startButton);
        stack.Children.Add(emptyDevices);
        return new Border { Child = stack };
    }

    private void ShowEmptyState()
    {
        var idle = !controller.IsRunning && !controller.DemoMode;
        emptyState.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        if (!idle) return;
        // Gerçek kare varken yazılar okunsun diye kare hafifçe karartılır; kare yoksa düz yüzey yeter.
        emptyState.Background = Preview.HasFrame ? new SolidColorBrush(Color.FromArgb(0xA6, 0x0B, 0x0C, 0x10)) : Brushes.Transparent;
        emptyMessage.Visibility = Preview.HasFrame ? Visibility.Collapsed : Visibility.Visible;
        startButton.Content = controller.IsStarting ? Localizer.Current["Starting…"] : "Çeviriyi başlat";
        startButton.IsEnabled = !controller.IsStarting && controller.CanStart;
        emptyDevices.Children.Clear();
        var white = PerdeColors.Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        foreach (var item in DeviceStatus.Items(controller))
        {
            var line = item.Hint.Contains(item.Text, StringComparison.OrdinalIgnoreCase) ? item.Hint : $"{item.Text} · {item.Hint}";
            var row = DeviceStatus.Line(item.State, line, size: 13);
            row.HorizontalAlignment = HorizontalAlignment.Center;
            row.Margin = Sp.T("0", "xxs", "0", "0");
            row.ToolTip = item.State.Name();
            ((TextBlock)row.Children[1]).Foreground = white;
            ((TextBlock)row.Children[1]).MaxWidth = 520;
            emptyDevices.Children.Add(row);
        }
        if (!controller.CanStart)
        {
            var fix = new Button { Content = "Eksik olanı tamamla: Ayarlar › Cihazlar", HorizontalAlignment = HorizontalAlignment.Center, Margin = Sp.T("0", "m", "0", "0") };
            fix.SetResourceReference(StyleProperty, "TextLink");
            fix.Foreground = white;
            fix.Click += (_, _) => ShellWindow.Navigate(SettingsGroup.Devices);
            emptyDevices.Children.Add(fix);
        }
    }

    // ───── Başlık satırı ─────

    private void Refresh()
    {
        // Ekranı çevir düğmesinin kısayolu ipucunda (Ayarlar › Kısayollar).
        ScreenButton.ToolTip = HotkeyText.Title(controller.Settings, HotkeyAction.Screen, "Translate the screen (%@)");
        ShowTvMeta();
        ShowEmptyState();
        ShowNotices();
    }

    /// <summary>"TV'de şu an · 1920×1080 · katman modu" (ya da bu bilgisayarda).</summary>
    private void ShowTvMeta()
    {
        var parts = new List<string> { controller.ShowsOnTv ? "TV'de şu an" : "bu bilgisayarda" };
        if (controller.LastFrame is { } frame) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{frame.Width}×{frame.Height}"));
        if (controller.ShowsOnTv)
            parts.Add(controller.Settings.Style.DisplayMode switch
            {
                TvDisplayMode.Overlay => "katman modu",
                TvDisplayMode.InAppOnly => "uygulama içi",
                _ => "otomatik mod",
            });
        TvMeta.Text = string.Join(" · ", parts);
    }

    /// <summary>
    /// Başlıkta oyunun adı (çeviri sürerken; bilinmiyorsa "Oyun tanınmadı" ve "Seç") ve "Hangi oyunu oynuyorsun?" seçicisi
    /// (oyunun kayıtlı alanları bildirim şeridinde).
    /// </summary>
    private void ShowGame()
    {
        var text = Localizer.Current;
        var running = controller.IsRunning && !controller.DemoMode;
        GameTitle.Text = controller.DemoMode ? text["Demo Mode"] : controller.PlayedGame ?? (running ? "Oyun tanınmadı" : "Oyun görüntüsü");
        GameChangeButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        GameChangeButton.Content = controller.PlayedGame is null ? "Oyunu seç" : "Değiştir";
        var asking = controller.AskingGame;
        var wasAsking = GameAsk.Visibility == Visibility.Visible;
        GameAsk.Visibility = asking ? Visibility.Visible : Visibility.Collapsed;
        Body.Opacity = asking ? 0.4 : 1;
        if (asking && !wasAsking)
        {
            picker.Open();
            Theme.Motion.EnterFrom(picker, 12, Theme.Motion.Page);
        }
        else if (asking) picker.Refresh();
        ShowTvMeta();
    }

    // ───── Sayılar ─────

    /// <summary>
    /// Tek satır sayılar: ortalama gecikme (oturumun ölçümleri; yoksa son satırlar), ortalama okuma hızı / 17 ve dökümün
    /// başındaki "412 satır · 38 düzeltme".
    /// </summary>
    private void ShowStats()
    {
        var stats = controller.SessionStats;
        var delays = controller.RecentDelaysMs;
        var delay = stats.AverageLatencyMs ?? (delays.Count == 0 ? null : (int)Math.Round(delays.Average()));
        DelayValue.Text = delay is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms} ms") : "– ms";
        SpeedValue.Text = stats.AverageCps is { } cps
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(cps):0} / {ReadingSpeed.MaxCps:0} kr/sn")
            : string.Create(CultureInfo.InvariantCulture, $"– / {ReadingSpeed.MaxCps:0} kr/sn");
        FeedCount.Text = string.Create(CultureInfo.InvariantCulture, $"{stats.Lines} satır · {stats.Corrections} düzeltme");
        ShowHideHint();
    }

    /// <summary>Görünüm bağlantısı: "İngilizcenin üstünde · 32 px · kenarlık 2 px".</summary>
    private void ShowLook() => LookLink.Content = LivePreviewLayout.Summary(controller.Settings.Style);

    /// <summary>Altyazıyı gizle kısayolu, kullanıcının gerçek tuşlarıyla (Ayarlar › Kısayollar); kısayol yoksa gizli.</summary>
    private void ShowHideHint()
    {
        var keys = HotkeyTable.KeysFor(controller.Settings.Hotkeys, HotkeyAction.HideSubtitles);
        HideHint.Visibility = keys.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (keys.Count == 0) return;
        var label = keys[0].Label;
        if (HideKeys.Tag as string == label) return;
        HideKeys.Tag = label;
        HideKeys.Children.Clear();
        foreach (var part in label.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var cap = new Border { Child = new TextBlock { Text = part.Trim() } };
            cap.SetResourceReference(StyleProperty, "Kbd");
            HideKeys.Children.Add(cap);
        }
        System.Windows.Automation.AutomationProperties.SetName(HideHint, $"{label}: altyazıyı gizle");
    }

    // ───── Döküm ─────

    /// <summary>Döküm: oturumun satırları (çeviri yokken son oturumunkiler), en yenisi üstte; ekrandaki satırlar işaretli.</summary>
    private void ShowFeed()
    {
        var onScreen = stage.Lines.Where(line => !line.Paragraphs).Select(line => line.Text).ToList();
        var terms = controller.GlossaryTerms.Select(term => term.English).ToList();
        var cues = LiveTranscript.Build(controller.SessionHistory, controller.SessionStartedAt, onScreen,
            LiveTranscript.EngineLabel(controller.Settings.Engine), terms);
        Feed.ItemsSource = cues;
        FeedEmpty.Visibility = cues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnHistory(object sender, RoutedEventArgs e) => ShellWindow.Navigate(ShellTab.History);

    private void OnLook(object sender, RoutedEventArgs e) => ShellWindow.Navigate(ShellTab.Appearance);

    /// <summary>"Alanları düzenle": son kare üstünde kutularla düzenleme ekranı (pencerenin tamamını kaplar).</summary>
    private void OnEditAreas(object sender, RoutedEventArgs e) => ShellWindow.EditAreas();

    // ───── Bildirimler ─────

    /// <summary>Önceki çalışma yerel bir hatayla kapandıysa bir kez bildirilir (sonraki açılışta yinelenmez).</summary>
    private void OnCrashChanged() => Dispatcher.BeginInvoke(ShowNotices);

    private bool ocrInstalling;

    /// <summary>
    /// Bildirim şeridi: görünen bildirimler en önemlisi başta (Shell: <see cref="LiveNotices"/>; hata &gt; yapılacak &gt; bilgi).
    /// Saniyede bir ve durum değişince; yapı aynıysa yalnız iletiler yenilenir. Hata bildirimi varken destek alanı gizlenir.
    /// </summary>
    private void ShowNotices()
    {
        var crash = Services.CrashWatch.Pending is not null;
        var kinds = controller.Notices(crash);
        if (crash && kinds.Contains(LiveNoticeKind.Crash)) Services.CrashWatch.MarkShown();
        var free = controller.Settings.Engine == TranslationEngine.Free;
        // Boş hâlde sarı "Çeviriyi başlat" varken şeritteki düğme çerçeveli kalır (ekranda tek sarı düğme).
        Strip.PrimaryAllowed = controller.IsRunning || controller.DemoMode || !controller.CanStart;
        Strip.Show([.. kinds.Select(kind => Describe(kind, LiveNotices.Level(kind, free)))]);
        ShowSupport();
    }

    /// <summary>Bildirimin iletisi, tek eylemi ve ayrıntısı (eski kartların metinleri ve düğmeleri).</summary>
    private NoticeItem Describe(LiveNoticeKind kind, LiveNoticeLevel level)
    {
        var text = Localizer.Current;
        switch (kind)
        {
            case LiveNoticeKind.EngineFailed:
                return new(kind, level, text["The free translation engine could not start on this computer."], "Motoru değiştir",
                    () => ShellWindow.Navigate(ShellTab.Translation), Details(["Çeviri sekmesinden Gemini (ücretsiz) ya da DeepL seç."]));
            case LiveNoticeKind.ModelMissing:
                if (controller.ModelDownloading)
                    return new(kind, level, string.Create(CultureInfo.InvariantCulture, $"Çeviri modeli iniyor · %{Math.Round(controller.ModelProgress * 100):0}"),
                        Signature: "model-downloading");
                return new(kind, level, text["The translation model is not downloaded yet."], text["Download the model"], controller.StartModelDownload,
                    Details([level == LiveNoticeLevel.Blocking
                        ? "Ücretsiz çeviri bu model olmadan çalışmaz. Model bir kez iner (yaklaşık 475 MB), sonra internetsiz çalışır."
                        : "Seçili motor çeviremediğinde satırlar bu modelle çevrilir. Model bir kez iner (yaklaşık 475 MB)."]));
            case LiveNoticeKind.OcrMissing:
                return new(kind, level, text["Text recognition for the game language is not installed on this computer."], text["Install text recognition"],
                    InstallOcr, Details([text["Without it Windows reads the subtitles with your system language and the translation turns into nonsense. Install it once; Windows asks for permission."]],
                        (text["Open Language and Region"], OpenLanguageSettings)), ActionEnabled: !ocrInstalling);
            case LiveNoticeKind.Console:
                return controller.NeedsXboxLink
                    ? new(kind, level, $"{controller.ConsoleTitle}: {controller.Ps5StatusText}", controller.XboxLinkTitle, controller.LinkXbox)
                    : new(kind, level, $"{controller.ConsoleTitle}: {controller.Ps5StatusText}", text["Connect Again"], () => controller.Reconnect());
            case LiveNoticeKind.WindowMissing:
                return new(kind, level, $"\"{controller.CaptureWindowTitle}\" penceresi açık değil ya da simge durumunda.", null, null,
                    WindowChoices, AutoExpand: true, Signature: "window:" + controller.CaptureWindowTitle);
            case LiveNoticeKind.PictureStopped:
            {
                var gap = controller.LastFrameAt is { } last ? (int)(DateTime.UtcNow - last).TotalSeconds : 0;
                return new(kind, level, text.Format("The PS5 picture hasn't arrived for %lld seconds.", gap), text["Reconnect Now"], () => controller.Reconnect(),
                    Details([text["The connection may have weakened. Çevirgen is trying to reconnect; if it doesn't, check that you're on the same Wi-Fi network as the PS5 and using 5 GHz."]],
                        (text["Open the Connection Test"], () => ShellWindow.Navigate(SettingsGroup.Devices))), Signature: "picture");
            }
            case LiveNoticeKind.Tv:
                return DescribeTv(kind, level);
            case LiveNoticeKind.Firewall:
                return DescribeFirewall(kind, level);
            case LiveNoticeKind.OnlineEngine:
            {
                var engine = controller.Settings.OnlineEngine;
                var reason = controller.OnlineIssue is { } issue ? issue.Describe(engine, text, controller.Settings.LocalAiServer) + " " : "";
                return new(kind, level, text.Format("%@ is not translating right now", engine.DisplayName()), "Çeviri ayarları",
                    () => ShellWindow.Navigate(ShellTab.Translation), Details([reason + text["Meanwhile the free translation is used."]]));
            }
            case LiveNoticeKind.SubtitlesOff:
            {
                // Çevirgen: GTA 6'da altyazı varsayılan kapalı; açma yolu ayrıntının başında, ayrıntı kendiliğinden açık.
                var gameHint = GameAreaDefaults.SubtitleSettingHint(controller.PlayedGame);
                return new(kind, level, text["Game Subtitles Off?"], text["Draw the Area Manually"], ShellWindow.EditAreas,
                    Details(gameHint is null
                        ? [text["Çevirgen reads the English subtitles on screen, not the audio. Turn on the game's own subtitles."]]
                        : [gameHint, text["Çevirgen reads the English subtitles on screen, not the audio. Turn on the game's own subtitles."]]),
                    AutoExpand: gameHint is not null);
            }
            case LiveNoticeKind.AreaProfileOffer:
                return new(kind, level, text.Format("Saved areas for %@ are available. Use them?", controller.OfferedAreaProfileGame ?? ""), text["Use"],
                    controller.UseOfferedAreaProfile, DismissLabel: text["No"], Dismiss: controller.DeclineOfferedAreaProfile);
            case LiveNoticeKind.MenuOpen:
            {
                var on = controller.Settings is { AutoScreenText: true, ScreenTextMenus: true };
                return new(kind, level, text["A menu is open in the game; there's no subtitle."], on ? null : text["Translate Menus Too (On-Screen Text)"],
                    on ? null : () => controller.Settings = controller.Settings with { AutoScreenText = true, ScreenTextMenus = true },
                    Details([text["We don't translate the text in the pause menu. The subtitles come back by themselves when you return to the game."]]));
            }
            case LiveNoticeKind.Crash:
                return new(kind, level, text["Çevirgen closed unexpectedly last time."], text["Show crash record"], Services.CrashWatch.ShowInExplorer,
                    Details([text["A crash record was saved on this computer. It stays here and is never sent anywhere on its own; you can add it when you report the problem."]],
                        (text["Copy summary"], () => Services.CrashWatch.CopySummary())),
                    DismissLabel: text["Close"], Dismiss: () =>
                    {
                        Services.CrashWatch.Dismiss();
                        ShowNotices();
                    });
            case LiveNoticeKind.AreaProfileActive:
                return new(kind, level, text.Format("Using the saved areas for %@.", controller.AreaProfileGame ?? ""), text["Undo"], controller.UndoAreaProfile);
            case LiveNoticeKind.GpuFallback:
                return new(kind, level, text["GPU could not be used; free translation is running on CPU."]);
            case LiveNoticeKind.RemotePlayPopup:
                // Çevirgen: karede PS5'in Uzaktan Oynatma bildirimi okundu. Kapatmak yalnız bu oturum için gizler.
                return new(kind, level, RemotePlayHint.Headline, Details: Details([RemotePlayHint.Instruction, RemotePlayHint.Accounts]),
                    DismissLabel: text["Close"], Dismiss: () =>
                    {
                        controller.DismissRemotePlayHint();
                        ShowNotices();
                    }, AutoExpand: true);
            case LiveNoticeKind.MainAccountTip:
                return new(kind, level, "Kol kapanmasın: PS5'te oyunu ana hesabınla başlat, oynarken hesap değiştirme.", Details: Details(
                    [text["To play without the connection dropping: 1. On the PS5, start the game with your MAIN account first. 2. Then start translating. 3. Don't switch users during the game; when you turn on the controller, choose your MAIN account. Don't play with the second account Çevirgen signs in with, or the controller will turn off by itself or won't work in the game."]]));
            case LiveNoticeKind.AutoScreenText:
                return new(kind, level, text["Automatic on-screen text translation is experimental; you can turn it on in Settings."]);
            default:
                return new(kind, level, "Kurulum bitmedi. Konsol olmadan örnek görüntülerle deneyebilirsin.", text["Try demo mode (no console needed)"],
                    StartDemo, ActionEnabled: !controller.IsStarting);
        }
    }

    /// <summary>
    /// TV: güncelleniyor (ilerleme kartı ayrıntıda açık), eşleşmedi, uygulaması eski, çeviri sürerken bağlı değil ya da son deneme
    /// altyazısı gitmedi. Diğer TV düğmeleri (yeniden kur, bağlan, değiştir) ayrıntıda.
    /// </summary>
    private NoticeItem DescribeTv(LiveNoticeKind kind, LiveNoticeLevel level)
    {
        var text = Localizer.Current;
        // Çevirgen: güncelleme bitti ama TV bağlı değil: "TV uygulamasını aç" öne geçer.
        if (controller.TvAppUpdating && !(controller.ShowTvAppUpdateDone && controller.ShowTvOpenHint))
            return new(kind, level, controller.ShowTvAppUpdateDone ? "TV uygulaması güncel." : "TV uygulaması güncelleniyor…",
                controller.ShowTvAppUpdateDone ? text["Close"] : null, controller.ShowTvAppUpdateDone ? controller.DismissTvAppUpdate : null,
                () => new Steps.TvAppCard(), AutoExpand: true);
        if (controller.NeedsTv)
            return new(kind, level, "TV eşleşmedi; altyazı TV'ye gidemiyor.", text["Connect TV"], () => controller.RunSetupStep(OnboardingStep.Tv));
        if (controller.ShowTvAppUpdate)
            return new(kind, level, controller.TvStatusText, text["Update the TV App"], UpdateTvApp, TvDetails());
        if (controller.ShowTvOpenHint)
            return controller.OpeningTvAppForTest
                ? new(kind, level, "TV uygulaması açılıyor, bağlanması bekleniyor…", "Açılıyor…", null, TvDetails(), ActionEnabled: false)
                : new(kind, level, controller.TvOpenTimedOut ? "TV uygulaması bağlanmadı." : controller.TvStatusText,
                    text["Open the TV App"], controller.RetryTvOpen, TvDetails(), AutoExpand: controller.TvOpenTimedOut);
        return new(kind, level, "Deneme altyazısı TV'ye gitmedi: TV uygulaması bağlı değil.", text["Connect TV"],
            () => controller.RunSetupStep(OnboardingStep.Tv), TvDetails());
    }

    private Func<FrameworkElement> TvDetails() => () =>
    {
        var text = Localizer.Current;
        var lines = new List<string>();
        if (controller.ShowTvOpenHint && controller.TvOpenHintText.Length > 0) lines.Add(controller.TvOpenHintText);
        if (controller.ShowTvOpenHint && controller.TvOpenTimedOut)
            lines.Add("Kumandayla aç: TV'nin ana ekranında Uygulamalar'dan \"Çevirgen\"i başlat (eski \"CouchSubs\" değil). Açılınca birkaç saniyede bağlanır; TV 6 haneli kod gösterirse bu bilgisayarda açılan onay penceresinden onayla.");
        if (controller.HasTvOpenReason) lines.Add(controller.TvOpenReasonText);
        if (controller.ShowTestTvHint)
            lines.Add(text["The test subtitle did not reach the TV because the TV app is not connected. Open Çevirgen on the TV. If the TV shows a 6-digit code, don't type it anywhere: approve it in the “Pair this TV?” window that opens on this computer, then test again."]);
        var buttons = new List<(string, Action)>();
        if (controller.CanReinstallTvApp) buttons.Add((text["Reinstall the TV App"], UpdateTvApp));
        if (controller.CanReconnectTv) buttons.Add((text["Connect TV"], () => controller.RunSetupStep(OnboardingStep.Tv)));
        if (controller.CanChangeTv) buttons.Add((text["Change the TV"], () => controller.RunSetupStep(OnboardingStep.Tv)));
        return Details([.. lines], [.. buttons])();
    };

    /// <summary>Güvenlik duvarı (FirewallCard ile aynı metinler): izin düğmesi yalnız sorulabilirken.</summary>
    private NoticeItem DescribeFirewall(LiveNoticeKind kind, LiveNoticeLevel level)
    {
        var text = Localizer.Current;
        var prompt = controller.FirewallPrompt;
        var running = controller.IsRunning;
        var headline = prompt == FirewallPrompt.Allowed
            ? text["Local-network permission is ready for Private networks."]
            : text["Windows Firewall may be blocking the connection."];
        var hint = prompt switch
        {
            FirewallPrompt.PublicNetwork => text["Permission applies only to Private networks. If this is your trusted home network, set it to Private in Windows Settings."],
            FirewallPrompt.Working => text["Waiting for Windows…"],
            FirewallPrompt.Failed => text["Local-network permission could not be verified. Approve the Windows prompt, or review existing blocking rules in Windows Firewall."],
            FirewallPrompt.Allowed when running => text["Reconnecting…"],
            FirewallPrompt.Allowed => text["Open Pair Device again on the PS5 and enter the new code."],
            _ when running && controller.IsXbox => text["Çevirgen needs Windows Firewall permission for incoming connections from the TV."],
            _ when running => text["Çevirgen needs incoming connections from the PS5 and the TV. Windows asks for permission once."],
            _ => text["Allow Çevirgen, then open Pair Device again on the PS5 and enter the new code."],
        };
        var canAllow = prompt is FirewallPrompt.Offer or FirewallPrompt.Failed or FirewallPrompt.Working;
        return new(kind, level, headline, canAllow ? text["Allow Çevirgen through Windows Firewall"] : null,
            canAllow ? AllowFirewall : null, Details([hint]), ActionEnabled: prompt != FirewallPrompt.Working);
    }

    private async void AllowFirewall() => await controller.AllowThroughFirewallAsync();

    /// <summary>Ayrıntı: açıklama satırları ve çerçeveli ek düğmeler.</summary>
    /// <summary>
    /// Çevirgen: "pencere açık değil" bildiriminin ayrıntısı: oyunu aç ya da açık pencerelerden birini seç (Ayarlar'a gitmeden).
    /// Tam ekran oyun Çevirgen'e geçince küçülür; oyuna dönünce okuma kendiliğinden başlar.
    /// </summary>
    private FrameworkElement WindowChoices()
    {
        var windows = controller.AvailableCaptureWindows().Where(window => window != controller.Settings.CaptureWindow).Take(8).ToList();
        var buttons = windows.Select(window => (window.Title, (Action)(() => { controller.ChooseCaptureWindow(window); ShowNotices(); }))).ToList();
        buttons.Add(("Yenile", ShowNotices));
        string[] lines =
        [
            "Oyunu aç ya da oyuna geri dön: tam ekran oyunlar Çevirgen'e geçince küçülür, oyuna dönünce okuma kendiliğinden başlar.",
            windows.Count > 0 ? "Başka bir pencere okunacaksa aşağıdan seç:" : "Şu an seçilebilecek başka pencere yok; oyunu açıp Yenile'ye bas.",
        ];
        return Details(lines, [.. buttons])();
    }

    private static Func<FrameworkElement> Details(string[] lines, params (string Label, Action Run)[] buttons) => () =>
    {
        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var block = new TextBlock { Text = line, Margin = Sp.T("0", "0", "0", "xs") };
            block.SetResourceReference(StyleProperty, "HintText");
            panel.Children.Add(block);
        }
        if (buttons.Length > 0)
        {
            var row = new WrapPanel { Margin = Sp.T("0", "xxs", "0", "0") };
            foreach (var (label, run) in buttons)
            {
                var button = new Button { Content = label, MinHeight = 32, Margin = Sp.T("0", "0", "xs", "xs") };
                button.SetResourceReference(StyleProperty, "OutlineButton");
                button.Click += (_, _) => run();
                row.Children.Add(button);
            }
            panel.Children.Add(row);
        }
        return panel;
    };

    // ───── Destek ─────

    /// <summary>
    /// Destek alanı (dökümün altında): yeterince satır çevrildiyse ve hata bildirimi yoksa; tek satır, çerçeveli düğme,
    /// isteğe bağlı simge. Kapatılamaz. Metin sunucudan değişebilir (<see cref="SupportSlot"/>).
    /// </summary>
    private void ShowSupport()
    {
        var slot = controller.Support;
        var visible = SupportSlot.Visible(controller.SupportLines, Strip.TopLevel == LiveNoticeLevel.Blocking);
        SupportRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        SupportText.Text = slot.Text;
        SupportButton.Content = slot.Button;
        SupportButton.ToolTip = slot.Link.Host + slot.Link.AbsolutePath.TrimEnd('/');
        if (SupportIcon.Tag as string != slot.IconFile)
        {
            SupportIcon.Tag = slot.IconFile;
            SupportIcon.Source = slot.IconFile is { } file ? LoadIcon(file) : null;
        }
        SupportIcon.Visibility = SupportIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSupport(object sender, RoutedEventArgs e) => controller.OpenSupport();

    private static System.Windows.Media.Imaging.BitmapSource? LoadIcon(string file)
    {
        try
        {
            if (!System.IO.File.Exists(file)) return null;
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 40;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException
                                          or InvalidOperationException)
        {
            return null;
        }
    }

    private async void StartDemo()
    {
        try { await controller.StartDemoAsync(); }
        catch (Exception error)
        {
            StartupGuard.Log("Deneme modu başlatılamadı", error);
            StartupGuard.Show(Application.Current, StartupGuard.ErrorMessage(error));
        }
    }

    private async void UpdateTvApp() => await controller.UpdateTvAppAsync();

    /// <summary>
    /// Oyun dilinin Windows yazı tanıma paketini yönetici izniyle kurar (Add-WindowsCapability), sonra okumayı
    /// yeni paketle yeniden başlatır.
    /// </summary>
    private async void InstallOcr()
    {
        var language = controller.Settings.SourceLanguage;
        var script = $"Get-WindowsCapability -Online | Where-Object {{ $_.Name -like 'Language.OCR~~~{language}-*' -and $_.State -ne 'Installed' }} | Select-Object -First 1 | Add-WindowsCapability -Online";
        ocrInstalling = true;
        ShowNotices();
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is not null) await process.WaitForExitAsync();
            if (!controller.IsRunning) return;
            controller.StopForRestart();
            await controller.StartAsync();
        }
        catch (System.ComponentModel.Win32Exception) { /* izin verilmedi */ }
        finally
        {
            ocrInstalling = false;
            ShowNotices();
        }
    }

    private static void OpenLanguageSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });

    private void OnTranslationSettings(object sender, RoutedEventArgs e) => ShellWindow.Navigate(ShellTab.Translation);

    /// <summary>Ekranı Çevir (kısayoldaki gibi): ekranın tamamı bir kez çevrilir.</summary>
    private async void OnTranslateScreen(object sender, RoutedEventArgs e)
    {
        try { await controller.TranslateScreenAsync(); }
        catch (Exception error) { StartupGuard.Log("Ekran çevrilemedi", error); }
    }

    /// <summary>Deneme altyazısı (çeviri başlamadıysa altyazı sunucusu da açılır).</summary>
    private async void OnTest(object sender, RoutedEventArgs e)
    {
        try { await controller.SendTestSubtitleAsync(); }
        catch (Exception error) { StartupGuard.Log("Deneme altyazısı gönderilemedi", error); }
    }

    // ───── Oyun ─────

    private void OnChangeGame(object sender, RoutedEventArgs e) => controller.AskGame();
}
