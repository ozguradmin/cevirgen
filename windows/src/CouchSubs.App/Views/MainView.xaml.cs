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
using CouchSubs.App.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

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
    private readonly StackPanel emptyDevices = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = Sp.T("0", "m", "0", "0") };
    private readonly Button startButton = new() { Content = "Çeviriyi başlat", MinHeight = 56, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };

    public MainView(AppController controller)
    {
        this.controller = controller;
        DataContext = controller;
        InitializeComponent();
        stage = new LiveStage(controller);
        stage.Changed += ShowStage;
        emptyState = BuildEmptyState();
        Preview.Overlay = emptyState;
        controller.PropertyChanged += OnChanged;
        connectionWatch.Tick += async (_, _) => await controller.CheckConnectionsAsync();
        tick.Tick += (_, _) => OnTick();
        Loaded += (_, _) =>
        {
            stage.Attach();
            connectionWatch.Start();
            tick.Start();
            Preview.SetFrame(controller.LastFrame);
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
        ShowCrash();
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
        if (e.PropertyName is nameof(AppController.NeedsXboxLink) or nameof(AppController.OffersReconnect) or nameof(AppController.ShowsOnTv)
            or nameof(AppController.NeedsTv) or nameof(AppController.CanReconnectTv) or nameof(AppController.ShowTvAppUpdate)
            or nameof(AppController.CanReinstallTvApp) or nameof(AppController.ShowTvOpenHint) or nameof(AppController.ShowTestTvHint)
            or nameof(AppController.TvAppUpdating) or nameof(AppController.TvClients) or nameof(AppController.IsRunning)
            or nameof(AppController.Settings) or nameof(AppController.CanStart) or nameof(AppController.DemoMode))
            ShowNotices();
    }

    /// <summary>Saniyede bir: sessizlik nedeni ve sayılar.</summary>
    private void OnTick()
    {
        ShowProblem();
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
        ShowFeed();
    }

    /// <summary>Çeviri kapalıyken önizlemenin üstünde: büyük sarı "Çeviriyi başlat" ve bağlı olanlar (taslak: boş hâl).</summary>
    private Border BuildEmptyState()
    {
        startButton.SetResourceReference(StyleProperty, "PrimaryButton");
        startButton.Padding = Sp.T("xl", "0", "xl", "0");
        startButton.Click += async (_, _) => await ShellWindow.ToggleTranslationAsync(controller);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(startButton);
        stack.Children.Add(emptyDevices);
        var shade = new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0F, 0x10, 0x14)), Child = stack };
        return shade;
    }

    private void ShowEmptyState()
    {
        var idle = !controller.IsRunning && !controller.DemoMode;
        emptyState.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        if (!idle) return;
        startButton.Content = controller.IsStarting ? Localizer.Current["Starting…"] : "Çeviriyi başlat";
        startButton.IsEnabled = !controller.IsStarting && controller.CanStart;
        emptyDevices.Children.Clear();
        foreach (var item in DeviceStatus.Items(controller))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = Sp.T("0", "xxs", "0", "0") };
            var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("0", "0", "xs", "0") };
            dot.SetResourceReference(Shape.FillProperty, ShellWindow.ToneBrush(item.Tone));
            row.Children.Add(dot);
            var line = item.Hint.Contains(item.Text, StringComparison.OrdinalIgnoreCase) ? item.Hint : $"{item.Text} · {item.Hint}";
            row.Children.Add(new TextBlock { Text = line, FontSize = 13, Foreground = Brushes.White, TextWrapping = TextWrapping.NoWrap,
                MaxWidth = 520, TextTrimming = TextTrimming.CharacterEllipsis });
            emptyDevices.Children.Add(row);
        }
        if (!controller.CanStart)
        {
            var why = new TextBlock
            {
                Text = "Başlamak için eksik olanı tamamla: Ayarlar › Cihazlar.", FontSize = 13, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = Sp.T("0", "s", "0", "0"),
            };
            emptyDevices.Children.Add(why);
        }
    }

    // ───── Başlık satırı ─────

    private void Refresh()
    {
        var text = Localizer.Current;
        var running = controller.IsRunning;
        ModelCard.Visibility = controller.ModelInstalled ? Visibility.Collapsed : Visibility.Visible;
        EngineCard.Visibility = controller.TranslatorStatus == TranslatorStatus.EngineFailed ? Visibility.Visible : Visibility.Collapsed;
        // Çevrimiçi motor çeviremiyor; satırlar bu arada cihaz üstü modelle geliyor.
        var issue = running ? controller.OnlineIssue : null;
        OnlineCard.Visibility = issue is null ? Visibility.Collapsed : Visibility.Visible;
        if (issue is { } state)
        {
            var engine = controller.Settings.OnlineEngine;
            OnlineTitle.Text = text.Format("%@ is not translating right now", engine.DisplayName());
            OnlineReason.Text = state.Describe(engine, text, controller.Settings.LocalAiServer) + " " + text["Meanwhile the free translation is used."];
        }
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
    /// Başlıkta oyunun adı (çeviri sürerken; bilinmiyorsa "Oyun tanınmadı" ve "Seç"), "Hangi Oyun?" kartı ve oyunun kayıtlı
    /// alanları.
    /// </summary>
    private void ShowGame()
    {
        var text = Localizer.Current;
        var running = controller.IsRunning && !controller.DemoMode;
        GameTitle.Text = controller.DemoMode ? text["Demo Mode"] : controller.PlayedGame ?? (running ? "Oyun tanınmadı" : "TV görüntüsü");
        GameChangeButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        GameChangeButton.Content = controller.PlayedGame is null ? "Oyunu seç" : "Değiştir";
        var asking = controller.AskingGame;
        var wasAsking = GameAsk.Visibility == Visibility.Visible;
        GameAsk.Visibility = asking ? Visibility.Visible : Visibility.Collapsed;
        Body.Opacity = asking ? 0.4 : 1;
        if (asking && !wasAsking) Theme.Motion.EnterFrom((FrameworkElement)GameAsk.Children[0], 12, Theme.Motion.Page);
        if (asking) ShowGameSuggestions();
        else GameSearch.Text = "";
        ShowAreaProfile(asking);
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
    private void OnCrashChanged() => Dispatcher.BeginInvoke(ShowCrash);

    private void ShowCrash()
    {
        var show = Services.CrashWatch.Pending is not null;
        CrashCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) Services.CrashWatch.MarkShown();
    }

    private void OnShowCrash(object sender, RoutedEventArgs e) => Services.CrashWatch.ShowInExplorer();

    private void OnCopyCrash(object sender, RoutedEventArgs e)
    {
        if (Services.CrashWatch.CopySummary()) CrashCopyButton.Content = Localizer.Current["Copied"];
    }

    private void OnDismissCrash(object sender, RoutedEventArgs e)
    {
        Services.CrashWatch.Dismiss();
        ShowCrash();
    }

    /// <summary>Konsol ve TV bildirimleri yalnız yapılacak bir şey varken görünür.</summary>
    private void ShowNotices()
    {
        ConsoleCard.Visibility = controller.NeedsXboxLink || controller.OffersReconnect ? Visibility.Visible : Visibility.Collapsed;
        var tvNeedsWork = controller.ShowsOnTv && (controller.NeedsTv || controller.CanReconnectTv && controller.IsRunning || controller.ShowTvAppUpdate
            || controller.CanReinstallTvApp || controller.ShowTvOpenHint || controller.ShowTestTvHint || controller.TvAppUpdating);
        TvCard.Visibility = tvNeedsWork ? Visibility.Visible : Visibility.Collapsed;
        DemoButton.Visibility = !controller.CanStart && !controller.IsRunning && !controller.DemoMode ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// "Sorun Var" (HomeProblem): görüntü durdu, menü açık ya da oyunun altyazısı kapalı; çözüm düğmeleri işi yapar ya da
    /// ilgili ayarı açar.
    /// </summary>
    private void ShowProblem()
    {
        var text = Localizer.Current;
        var now = DateTime.UtcNow;
        var kind = HomeProblem.For(controller.IsRunning, controller.DemoMode, controller.LastFrameAt is { } at ? now - at : null,
            controller.ScreenMenuOpen, controller.ShowsSubtitleOffTip);
        ProblemCard.Visibility = kind == HomeProblemKind.None ? Visibility.Collapsed : Visibility.Visible;
        if (kind == HomeProblemKind.None)
        {
            shownProblem = kind;
            return;
        }
        var lastLine = controller.History.FirstOrDefault()?.Date;
        var silent = HomeProblem.SilentSeconds(now, lastLine, controller.SessionStartedAt ?? now);
        SilentFor.Text = controller.IsRunning ? text.Format("%lld s silent", silent) : text["Issue"];
        var pictureGap = controller.LastFrameAt is { } last ? (int)(now - last).TotalSeconds : 0;
        switch (kind)
        {
            case HomeProblemKind.PictureStopped:
                ProblemTitle.Text = text.Format("The PS5 picture hasn't arrived for %lld seconds.", pictureGap);
                ProblemText.Text = text["The connection may have weakened. CouchSubs is trying to reconnect; if it doesn't, check that you're on the same Wi-Fi network as the PS5 and using 5 GHz."];
                break;
            case HomeProblemKind.MenuOpen:
                ProblemTitle.Text = text["A menu is open in the game; there's no subtitle."];
                ProblemText.Text = text["We don't translate the text in the pause menu. The subtitles come back by themselves when you return to the game."];
                break;
            default:
                ProblemTitle.Text = text["Game Subtitles Off?"];
                ProblemText.Text = text["CouchSubs reads the English subtitles on screen, not the audio. Turn on the game's own subtitles."];
                break;
        }
        if (kind == shownProblem) return;
        shownProblem = kind;
        ProblemFixes.Children.Clear();
        switch (kind)
        {
            case HomeProblemKind.PictureStopped:
                AddFix(text["Reconnect Now"], primary: true, () => controller.Reconnect());
                AddFix(text["Open the Connection Test"], primary: false, () => ShellWindow.Navigate(SettingsGroup.Devices));
                break;
            case HomeProblemKind.MenuOpen:
                var on = controller.Settings is { AutoScreenText: true, ScreenTextMenus: true };
                AddFix(text["Translate Menus Too (On-Screen Text)"] + (on ? " · " + text["Completed"] : ""), primary: !on,
                    () => controller.Settings = controller.Settings with { AutoScreenText = true, ScreenTextMenus = true });
                break;
            default:
                AddFix(text["Draw the Area Manually"], primary: false, ShellWindow.EditAreas);
                break;
        }
    }

    private HomeProblemKind shownProblem;

    /// <summary>Çözüm düğmesi; ilki sarı.</summary>
    private void AddFix(string label, bool primary, Action run)
    {
        var button = new Button { Content = label, Margin = Sp.T("0", "0", "xs", "xs") };
        button.SetResourceReference(StyleProperty, primary ? "PrimaryButton" : "OutlineButton");
        button.Click += (_, _) =>
        {
            run();
            shownProblem = HomeProblemKind.None;
            ShowProblem();
        };
        ProblemFixes.Children.Add(button);
    }

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

    /// <summary>TV'siz kurulmuş uygulamada TV'yi sonradan bağlamak ya da TV'yi değiştirmek için sihirbazın TV adımı açılır.</summary>
    private void OnConnectTv(object sender, RoutedEventArgs e) => controller.RunSetupStep(OnboardingStep.Tv);

    /// <summary>TV uygulaması bağlı değil: bekçi TV uygulamasını beklemeden yeniden açmaya çalışır.</summary>
    private void OnRetryTvOpen(object sender, RoutedEventArgs e) => controller.RetryTvOpen();
    private void OnLinkXbox(object sender, RoutedEventArgs e) => controller.LinkXbox();

    private void OnReconnect(object sender, RoutedEventArgs e) => controller.Reconnect();

    // "Yeniden kur" ve "Güncelle" ana ekranda kurar (kart TV bildiriminin altında açılır); TV adımına gidilmez.
    private async void OnReinstallTvApp(object sender, RoutedEventArgs e) => await controller.UpdateTvAppAsync();

    private async void OnUpdateTvApp(object sender, RoutedEventArgs e) => await controller.UpdateTvAppAsync();

    private void OnDismissTvAppUpdate(object sender, RoutedEventArgs e) => controller.DismissTvAppUpdate();

    private void OnDownload(object sender, RoutedEventArgs e) => controller.StartModelDownload();

    /// <summary>
    /// Oyun dilinin Windows yazı tanıma paketini yönetici izniyle kurar (Add-WindowsCapability), sonra okumayı
    /// yeni paketle yeniden başlatır.
    /// </summary>
    private async void OnInstallOcr(object sender, RoutedEventArgs e)
    {
        var language = controller.Settings.SourceLanguage;
        var script = $"Get-WindowsCapability -Online | Where-Object {{ $_.Name -like 'Language.OCR~~~{language}-*' -and $_.State -ne 'Installed' }} | Select-Object -First 1 | Add-WindowsCapability -Online";
        InstallOcr.IsEnabled = false;
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
        finally { InstallOcr.IsEnabled = true; }
    }

    private void OnLanguageSettings(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });

    // ───── Oyun ─────

    /// <summary>
    /// Oyunun kayıtlı alanları: uygulandıysa "X için kayıtlı alanlar kullanılıyor · Geri al"; oyun yalnız paketten
    /// tanındıysa "X için kayıtlı alanlar var. Kullanılsın mı? · Kullan / Hayır" (Mac, iPhone ve Android ile aynı).
    /// </summary>
    private void ShowAreaProfile(bool asking)
    {
        var text = Localizer.Current;
        var offered = controller.OfferedAreaProfileGame;
        var active = controller.AreaProfileGame;
        AreaProfileRow.Visibility = controller.IsRunning && !asking && (offered ?? active) is not null ? Visibility.Visible : Visibility.Collapsed;
        AreaProfileUse.Visibility = AreaProfileNo.Visibility = offered is not null ? Visibility.Visible : Visibility.Collapsed;
        AreaProfileUndo.Visibility = offered is null && active is not null ? Visibility.Visible : Visibility.Collapsed;
        AreaProfileText.Text = offered is not null ? text.Format("Saved areas for %@ are available. Use them?", offered)
            : active is not null ? text.Format("Using the saved areas for %@.", active) : "";
    }

    private void OnUseAreaProfile(object sender, RoutedEventArgs e) => controller.UseOfferedAreaProfile();
    private void OnDeclineAreaProfile(object sender, RoutedEventArgs e) => controller.DeclineOfferedAreaProfile();
    private void OnUndoAreaProfile(object sender, RoutedEventArgs e) => controller.UndoAreaProfile();

    /// <summary>Kapak rafı: en çok beş öneri; paketi olan "Paket Var", son oynanan "Son Oynanan"; seçili oyun sarı halkalı.</summary>
    private void ShowGameSuggestions()
    {
        var text = Localizer.Current;
        var typed = GameSearch.Text.Trim();
        GameSearchHint.Visibility = typed.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var suggestions = controller.GameSuggestions(typed).Take(5).ToList();
        var last = controller.LastPlayedGame;
        var shelf = GameCatalog.PackShelf(controller.InstalledPacks);
        var chosen = controller.PlayedGame is { } played ? GameName.Key(played) : null;
        GameList.Children.Clear();
        foreach (var name in suggestions)
        {
            var hasPack = shelf.Any(item => item.Titles.Append(item.Title).Any(title => GameName.Key(title) == GameName.Key(name)));
            var status = hasPack ? text["Pack Available"] : name == last ? text["Last Played"] : null;
            var cover = new GameCover(name, status, GameName.Key(name) == chosen) { Margin = GameList.Children.Count == 0 ? new Thickness(0) : Sp.T("s", "0", "0", "0") };
            cover.Click += (_, _) => controller.ChooseGame(name);
            GameList.Children.Add(cover);
        }
    }

    /// <summary>Kapak rafı yatay kayar (kaydırma çubuğu gizli): fare tekerleği rafı yana kaydırır.</summary>
    private void OnShelfWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        GameShelf.ScrollToHorizontalOffset(GameShelf.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    private void OnChangeGame(object sender, RoutedEventArgs e) => controller.AskGame();

    private void OnDontAskGame(object sender, RoutedEventArgs e) => controller.DismissGame();

    private void OnGameSearch(object sender, TextChangedEventArgs e) => ShowGameSuggestions();

    private void OnGameSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || GameSearch.Text.Trim() is not { Length: > 0 } typed) return;
        controller.ChooseGame(typed);
        e.Handled = true;
    }

    /// <summary>"Listede Yok": yazılan ad kullanılır; bir şey yazılmadıysa arama kutusuna odaklanılır.</summary>
    private void OnCustomGame(object sender, RoutedEventArgs e)
    {
        if (GameSearch.Text.Trim() is { Length: > 0 } typed) controller.ChooseGame(typed);
        else GameSearch.Focus();
    }
}
