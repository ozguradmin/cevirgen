// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using CouchSubs.App.Theme;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CouchSubs.App.Services;
using CouchSubs.Core;
using CouchSubs.Core.LgTv;
using CouchSubs.Shell;
using Microsoft.Win32;

namespace CouchSubs.App.Views;

/// <summary>Ayarların doğrudan açılabilen eski bölüm adları (Canlı ekran, tepsi, sihirbaz); <see cref="SettingsView.GroupFor"/>.</summary>
public enum SettingsSection { Appearance, Translation, PictureSource }

/// <summary>Çevirgen Ayarlar'ın altı bölümü (taslak: Settings.dc.html), soldaki listenin sırasıyla.</summary>
public enum SettingsGroup { Devices, Areas, Dubbing, Shortcuts, Advanced, App }

public partial class SettingsView : UserControl
{
    private readonly AppController controller;
    private bool loading = true;
    /// <summary>Kısayollar'da yeni tuşu bekleyen yuva; null ise beklenmiyor.</summary>
    private (HotkeyAction Action, int Slot, Button Button)? recording;
    /// <summary>Kısayollar'daki düğmeler: iş ve yuvaya göre.</summary>
    private readonly Dictionary<(HotkeyAction, int), Button> shortcutButtons = [];

    internal SettingsView(AppController controller)
    {
        this.controller = controller;
        controller.NoteScreen("settings");
        InitializeComponent();
        InstalledVersion.Text = Localizer.Current.Format("Installed version: %@", AppVersion.Current(typeof(SettingsView).Assembly));
        NavVersion.Text = AppVersion.Current(Assembly.GetEntryAssembly() ?? typeof(SettingsView).Assembly);
        var settings = controller.Settings;
        DataContext = controller;
        MisreadWait.Value = settings.MisreadWaitMs / 1000.0;
        PartialWait.Value = settings.PartialWaitMs / 1000.0;
        ShowWaits();
        DisplayMode.SelectedItem = DisplayMode.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == settings.Style.DisplayMode.ToString());
        (settings.FreeAreaTarget switch { ResultTarget.Pc => FreePc, ResultTarget.Both => FreeBoth, _ => FreeTv }).IsChecked = true;
        ObsAddress.Text = controller.ObsAddress;
        LegacyTvWarning.Visibility = controller.LegacyTvWarning ? Visibility.Visible : Visibility.Collapsed;
        // Localhost'u kabul etmeyen yayın programları (TikTok LIVE Studio) için aynı adres bilgisayarın ağ adresiyle.
        if (controller.NetworkObsAddress is { } networkObs) NetworkObsAddress.Text = networkObs;
        else NetworkObsPanel.Visibility = Visibility.Collapsed;
        var streamBackground = controller.StreamBackground.ToString();
        StreamBackgroundChoice.SelectedItem = StreamBackgroundChoice.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == streamBackground);
        ShowAreaProfile();
        GameProfilesHint.Text = Localizer.Current["Each game keeps its own subtitle area and extra areas. A game that isn't in the list can be added by typing its name; one profile never overwrites another."];
        ShowGameProfiles();
        ShowAreasSummary();
        Dubbing.Attach(controller);
        FirewallSettings.Attach(controller);
        OutputCard.Attach(controller);
        SecondScreenCard.Attach(controller);
        ShowStyleTitle();
        controller.PropertyChanged += OnControllerChanged;
        Unloaded += (_, _) => controller.PropertyChanged -= OnControllerChanged;
        TranslationDeviceChoice.SelectedItem = TranslationDeviceChoice.Items.Cast<ComboBoxItem>()
            .First(item => (string)item.Tag == settings.TranslationDevice.ToString());
        foreach (var count in CpuThreadOptions.Available(Environment.ProcessorCount))
            CpuThreadChoice.Items.Add(new ComboBoxItem { Content = count.ToString(), Tag = count });
        CpuThreadChoice.SelectedItem = CpuThreadChoice.Items.Cast<ComboBoxItem>()
            .First(item => (int)item.Tag == CpuThreadOptions.Normalize(settings.CpuTranslationThreads, Environment.ProcessorCount));
        FastSubtitleDetection.IsChecked = settings.FastSubtitleDetection;
        AutoReadGamePhone.IsChecked = settings.AutoReadGamePhone;
        PhoneMessagesPanel.IsChecked = settings.PhoneMessagesPanel;
        PhoneMessagesBubbles.IsChecked = !settings.PhoneMessagesPanel;
        OcrEngineChoice.SelectedItem = OcrEngineChoice.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == settings.OcrEngine.ToString());
        ShowAdvancedOcr();
        ShowCpuThreads();
        GamePacks.IsChecked = settings.GamePacks;
        SharePool.IsChecked = settings.SharePool;
        UsageData.IsChecked = settings.UsageData;
        // Telefonlardaki Profil › Kullanım verisi ile aynı; ölçüm bu derlemede yoksa gizli.
        UsageDataRow.Visibility = controller.UsageConfigured ? Visibility.Visible : Visibility.Collapsed;
        AskGame.IsChecked = settings.AskGame;
        AutoScreenText.IsChecked = settings.AutoScreenText;
        ScreenTextPhone.IsChecked = settings.ScreenTextPhone;
        ScreenTextMission.IsChecked = settings.ScreenTextMission;
        ScreenTextHelp.IsChecked = settings.ScreenTextHelp;
        ScreenTextMenus.IsChecked = settings.ScreenTextMenus;
        ScreenTextOther.IsChecked = settings.ScreenTextOther;
        ScreenTextKindsPanel.Visibility = settings.AutoScreenText ? Visibility.Visible : Visibility.Collapsed;
        SetTextApart.IsChecked = settings.SetTextApart;
        GameAreaPresets.IsChecked = settings.GameAreaPresets;
        ShowPacks();
        (settings.Appearance switch { AppAppearance.Light => AppearanceLight, AppAppearance.System => AppearanceSystem, _ => AppearanceDark }).IsChecked = true;
        StartupSound.IsChecked = settings.StartupSound;
        TransitionAnimations.IsChecked = settings.TransitionAnimations;
        ShowTvStatus();
        Sections.SelectedIndex = 0;
        ShowHeading();
        RemotePlayAppChoice.SelectedItem = RemotePlayAppChoice.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == settings.RemotePlayApp.ToString());
        XboxAudioOnComputer.IsChecked = settings.XboxAudioOnComputer;
        XboxLegacyFrameRate.IsChecked = settings.XboxLegacyFrameRate;
        ShowConsole();
        ShowDisplayModeSegment();
        // Çevirgen: anonim sayım (KVKK açıklaması satırın altında) ve arayüz dili.
        AnonymousCount.IsChecked = controller.AnonymousCount;
        AnonymousCount.Description = AppController.AnonymousCountDisclosure;
        InterfaceLanguage.Text = Localizer.Current.Language == "tr" ? "Türkçe" : System.Globalization.CultureInfo.CurrentUICulture.NativeName;
        ShowPasscode();
        BuildShortcuts();
        ShowShortcutTexts();
        PreviewKeyDown += OnShortcutKey;
        ShowStreamWindowToggle();
        // Akış penceresi kendi düğmesiyle kapanmış olabilir; Ayarlar açılınca düğme güncellenir.
        Loaded += (_, _) => ShowStreamWindowToggle();
        Glow.Paused = controller.IsRunning;
        Loaded += (_, _) => DarkenAmberGlyph();
        Loaded += (_, _) => MoveSelection(false);
        NavHost.SizeChanged += (_, _) => MoveSelection(false);
        Unloaded += (_, _) => StopRecording();
        loading = false;
    }

    /// <summary>PS5 giriş şifresi kayıtlıysa yalnız "kaydedildi" görünür; şifrenin kendisi geri okunmaz.</summary>
    private void ShowPasscode()
    {
        var saved = controller.HasLoginPasscode;
        NewPasscode.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        SavedPasscode.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPasscodeSave(object sender, RoutedEventArgs e)
    {
        var accepted = controller.SetLoginPasscode(PasscodeBox.Password);
        PasscodeError.Visibility = accepted ? Visibility.Collapsed : Visibility.Visible;
        if (accepted) PasscodeBox.Clear();
        ShowPasscode();
    }

    private void OnPasscodeRemove(object sender, RoutedEventArgs e)
    {
        controller.RemoveLoginPasscode();
        ShowPasscode();
    }

    /// <summary>Remote Play uygulaması seçimi ve ipuçları yalnız seçili görüntü kaynağına göre görünür.</summary>
    private void ShowFrameSource()
    {
        // Xbox'ta (#853) seçenekler "Xbox (bu uygulama bağlanır)" ve "OBS projektörü veya oyun penceresi"; PS5'inkiler gizli.
        var xbox = controller.IsXbox;
        var settings = controller.Settings;
        var selectedTag = xbox ? (settings.XboxFrameSource == XboxFrameSource.CaptureWindow ? "CaptureWindow" : "Xbox") : settings.FrameSource.ToString();
        XboxSourceItem.Visibility = xbox ? Visibility.Visible : Visibility.Collapsed;
        Ps5SourceItem.Visibility = RemotePlaySourceItem.Visibility = xbox ? Visibility.Collapsed : Visibility.Visible;
        var wasLoading = loading;
        loading = true;
        FrameSourceChoice.SelectedItem = FrameSourceChoice.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == selectedTag);
        loading = wasLoading;
        XboxWindowHint.Visibility = xbox ? Visibility.Visible : Visibility.Collapsed;
        XboxPictureSourceCard.Visibility = xbox && !controller.XboxReadsWindow ? Visibility.Visible : Visibility.Collapsed;
        var window = !xbox && settings.FrameSource == FrameSource.RemotePlayWindow;
        var capture = xbox ? controller.XboxReadsWindow : settings.FrameSource == FrameSource.CaptureWindow;
        RemotePlayAppPanel.Visibility = window ? Visibility.Visible : Visibility.Collapsed;
        WindowSourceHint.Visibility = window ? Visibility.Visible : Visibility.Collapsed;
        CaptureWindowPanel.Visibility = capture ? Visibility.Visible : Visibility.Collapsed;
        if (capture) RefreshCaptureWindows();
        Ps5SourceHint.Visibility = xbox || window || capture ? Visibility.Collapsed : Visibility.Visible;
        SonyInstallRow.Visibility = ChiakiInstallRow.Visibility = window ? Visibility.Visible : Visibility.Collapsed;
        var sony = controller.RemotePlayInstalled(RemotePlayApp.Sony);
        var chiaki = controller.RemotePlayInstalled(RemotePlayApp.Chiaki);
        SonyInstalledText.Visibility = sony ? Visibility.Visible : Visibility.Collapsed;
        SonyDownloadButton.Visibility = sony ? Visibility.Collapsed : Visibility.Visible;
        ChiakiInstalledText.Visibility = chiaki ? Visibility.Visible : Visibility.Collapsed;
        ChiakiDownloadButton.Visibility = chiaki ? Visibility.Collapsed : Visibility.Visible;
        Ps5LinkPanel.Visibility = controller.NeedsPs5Link ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDownloadSony(object sender, RoutedEventArgs e) => OpenUrl(AppController.SonyRemotePlayDownload);
    private void OnDownloadChiaki(object sender, RoutedEventArgs e) => OpenUrl(AppController.ChiakiDownload);
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>"Hesabı bağla": sihirbaz ikinci hesap adımından açılır; ayar penceresi kapanır, sihirbaz ana pencerede görünür.</summary>
    /// <summary>Konsol kartı ve konsola bağlı bölümler (Mac Ayarlar › Konsol ile aynı).</summary>
    private void ShowConsole()
    {
        var xbox = controller.IsXbox;
        var wasLoading = loading;
        loading = true;
        (xbox ? ConsoleXbox : ConsolePlayStation).IsChecked = true;
        loading = wasLoading;
        // Kart her zaman görünür (Mac ConsoleSettingsCard); Xbox kapalıysa yalnız PS5 ↔ Xbox seçici gizlenir.
        // Çevirgen: PS5 ↔ Xbox seçici yerine "Platformu değiştir"; PS5 bilgisi Konsol bölümünün satırlarında.
        ConsoleTabs.Visibility = Visibility.Collapsed;
        ConsolePlayStationHint.Visibility = Visibility.Collapsed;
        ShowDeviceRows();
        var text = Localizer.Current;
        if (controller.Console is { } ps5)
        {
            ConsolePs5Row.Title = string.IsNullOrEmpty(ps5.Name) ? "PS5" : ps5.Name;
            ConsolePs5Row.Text = ps5.Host;
            // Eşleşmiş PS5 yalnız görüntü gelirken "Bağlı"dır; çeviri durmuşken "Eşleşti" (tek renk modeli).
            var state = controller.IsXbox ? DeviceState.Ready : controller.SourceState;
            ConsolePs5Badge.Text = controller.IsRunning && state == DeviceState.Ready ? text["Connected"]
                : state == DeviceState.Waiting ? "Bağlanıyor" : state == DeviceState.Error ? "Bağlanamadı" : "Eşleşti";
            ConsolePs5Badge.Tone = state switch
            {
                DeviceState.Waiting => Controls.ChipTone.Amber,
                DeviceState.Error => Controls.ChipTone.Live,
                _ => Controls.ChipTone.Ok,
            };
        }
        else
        {
            ConsolePs5Row.Title = "PS5";
            ConsolePs5Row.Text = text["With Remote Play and a second PSN account."];
            ConsolePs5Badge.Text = text["Not Set Up"];
            ConsolePs5Badge.Tone = Controls.ChipTone.Muted;
        }
        ConsoleXboxPanel.Visibility = xbox ? Visibility.Visible : Visibility.Collapsed;
        ConsoleXboxName.Text = controller.SelectedXboxConsole is { } console ? "  ·  " + (string.IsNullOrEmpty(console.Name) ? "Xbox" : console.Name) : "";
        // Microsoft hesap kartı (Mac DSAccountCard): girişliyse "Yeniden Giriş Yap" ve Diğer Giriş Yolları'nda Xbox seçimi ve çıkış.
        MicrosoftAccount.SignedIn = controller.HasXboxAccount;
        MicrosoftAccount.HasOthers = controller.HasXboxAccount;
        if (controller.HasXboxAccount && controller.NeedsXboxLink) MicrosoftAccount.Expanded = true;
        // TV girişi kartı: PS5'te ve HDMI'ye bağlı TV eşleşmişken (kurulumdaki TV Girişi adımı ile aynı koşul).
        HdmiCard.Visibility = !xbox && controller.NeedsHdmiStep ? Visibility.Visible : Visibility.Collapsed;
        var hdmiLoading = loading;
        loading = true;
        foreach (var input in new[] { Hdmi1, Hdmi2, Hdmi3, Hdmi4 })
            input.IsChecked = (string)input.Tag == controller.Settings.Style.HdmiInput?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        loading = hdmiLoading;
        ConsoleXboxLink.Content = Localizer.Current["Choose your Xbox"];
        ConsoleXboxSignedIn.Visibility = controller.HasXboxAccount ? Visibility.Visible : Visibility.Collapsed;
        ShowFrameSource();
        PasscodeTab.Visibility = xbox || controller.Platform != SetupPlatform.Ps5 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Cihazlar › Konsol satırları (taslak): platform ve "Platformu değiştir"; PS5 adı, adresi ve eşleşmesi ("Başka PS5 ekle",
    /// "Eşleşmeyi kaldır"); ikinci PSN hesabı ("Hesabı değiştir"); Xbox (bağla / hesap kartı). Yalnız platforma uyanlar görünür.
    /// </summary>
    private void ShowDeviceRows()
    {
        var platform = controller.Platform;
        var option = SetupPlatforms.Option(platform);
        PlatformName.Text = $"Platform: {option.Name}";
        PlatformDesc.Text = option.How;
        var ps5 = platform == SetupPlatform.Ps5;
        Ps5Item.Visibility = PsnItem.Visibility = ps5 ? Visibility.Visible : Visibility.Collapsed;
        if (controller.Console is { } console)
        {
            Ps5Name.Text = string.IsNullOrEmpty(console.Name) ? "PS5" : console.Name;
            Ps5Desc.Text = $"{console.Host} · eşleşti";
        }
        else
        {
            Ps5Name.Text = "PS5 eşleşmedi";
            Ps5Desc.Text = "Uzaktan Oynatma'yı aç, PS5'te Ayarlar › Sistem › Uzaktan Oynatma › Cihazı Eşle.";
        }
        AddPs5.Content = controller.Console is null ? "PS5 eşle" : "Başka PS5 ekle";
        ForgetPs5.Visibility = controller.Console is null ? Visibility.Collapsed : Visibility.Visible;
        ChangePsn.Content = controller.HasAccount ? "Hesabı değiştir" : "Hesabı bağla";
        var xbox = platform == SetupPlatform.Xbox;
        XboxItem.Visibility = xbox ? Visibility.Visible : Visibility.Collapsed;
        ConsoleCard.Visibility = xbox && controller.HasXboxAccount ? Visibility.Visible : Visibility.Collapsed;
        XboxDesc.Text = controller.HasXboxAccount
            ? controller.SelectedXboxConsole is { } box ? $"Microsoft hesabıyla bağlı · {(string.IsNullOrEmpty(box.Name) ? "Xbox" : box.Name)}" : "Microsoft hesabıyla bağlı · Xbox seçilmedi"
            : "Microsoft hesabıyla bağlanır. Bağlı değil.";
        XboxConnect.Content = controller.HasXboxAccount ? "Xbox seç" : "Xbox bağla";
    }

    /// <summary>"Platformu değiştir": sihirbaz platform sorusundan açılır; yalnız eksik adımlar gelir, öteki ayarlar kalır.</summary>
    private void OnChangePlatform(object sender, RoutedEventArgs e) => controller.BeginPlatformChange();

    /// <summary>"Başka PS5 ekle" / "PS5 eşle": PS5 eşleştirme adımı (hesap duruyorsa yalnız o).</summary>
    private void OnAddPs5(object sender, RoutedEventArgs e) =>
        controller.RunSetupStep(controller.HasAccount ? OnboardingStep.Console : OnboardingStep.PsnLogin);

    /// <summary>"Eşleşmeyi kaldır": onaydan sonra PS5 eşleşmesi silinir (PSN hesabı kalır).</summary>
    private void OnForgetPs5(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Host, "Bu PS5'in eşleşmesi kaldırılacak. Yeniden eşlemek için PS5'te yeni bir kod gerekir.", "Eşleşme kaldırılsın mı?",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        controller.ForgetConsole();
        ShowConsole();
    }

    /// <summary>"Hesabı değiştir": ikinci PSN hesabıyla yeniden giriş.</summary>
    private void OnChangePsn(object sender, RoutedEventArgs e) => controller.RunSetupStep(OnboardingStep.PsnLogin);

    /// <summary>"TV'yi değiştir": sihirbazın TV adımı (TV bulunur, eşleşir, uygulama kurulur).</summary>
    private void OnChangeTv(object sender, RoutedEventArgs e) => controller.RunSetupStep(OnboardingStep.Tv);

    /// <summary>"TV uygulamasını yeniden kur": Canlı ekrandaki TV bildiriminde ilerleme ve sonuç görünür.</summary>
    private async void OnReinstallTvApp(object sender, RoutedEventArgs e)
    {
        Leave();
        await controller.UpdateTvAppAsync();
    }

    /// <summary>Görüntü modu üçlü seçicisi (Otomatik / Uygulama içi / Katman) gizli açılır listeyi sürer.</summary>
    private void OnDisplayModeSegment(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not RadioButton { Tag: string tag }) return;
        DisplayMode.SelectedItem = DisplayMode.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == tag);
    }

    private void ShowDisplayModeSegment()
    {
        var mode = controller.Settings.Style.DisplayMode;
        var wasLoading = loading;
        loading = true;
        (mode switch { TvDisplayMode.InAppOnly => DisplayInApp, TvDisplayMode.Overlay => DisplayOverlay, _ => DisplayAuto }).IsChecked = true;
        loading = wasLoading;
    }

    /// <summary>Uygulama › Gizlilik › "Anonim kullanım sayımı" (kapatılınca kurulum kimliği silinir).</summary>
    private void OnAnonymousCount(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        controller.AnonymousCount = AnonymousCount.IsChecked == true;
    }

    /// <summary>"Günlükleri kaydet": günlükler maskelenip zip olur, zip panoya dosya olarak da konur; "Gezgin'de göster".</summary>
    private async void OnSaveLogs(object sender, RoutedEventArgs e)
    {
        SaveLogs.IsEnabled = false;
        SaveLogs.Content = Localizer.Current["Copying…"];
        var zip = await Services.LogArchive.CopyAsync();
        SaveLogs.IsEnabled = true;
        SaveLogs.Content = "Günlükleri kaydet";
        SaveLogsStatus.Visibility = Visibility.Visible;
        SaveLogsStatus.Text = zip is null ? Localizer.Current["The log couldn't be collected. Try again."]
            : "Günlükler kaydedildi ve panoya kopyalandı; hata bildirimine yapıştırabilirsin.";
        ShowLogs.Visibility = zip is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnShowLogs(object sender, RoutedEventArgs e) => Services.LogArchive.ShowInExplorer();

    /// <summary>"Güncellemeleri denetle": yeni sürüm varsa pencerenin en üstünde sarı şerit çıkar.</summary>
    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        CheckUpdate.IsEnabled = false;
        await controller.CheckForUpdateAsync();
        CheckUpdate.IsEnabled = true;
        UpdateStatus.Visibility = Visibility.Visible;
        UpdateStatus.Text = controller.UpdateVersion is { } version ? $"Yeni sürüm var: {version}. Pencerenin üstündeki sarı şeritten güncelleyebilirsin."
            : "Çevirgen güncel.";
    }

    private void OnConsoleKind(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not RadioButton { Tag: string tag }) return;
        controller.ChangeConsoleKind(tag == "Xbox" ? GameConsoleKind.Xbox : GameConsoleKind.PlayStation);
        ShowConsole();
    }

    private void OnLinkXbox(object sender, RoutedEventArgs e)
    {
        controller.LinkXbox();
        Leave();
    }

    private void OnSignOutXbox(object sender, RoutedEventArgs e)
    {
        controller.SignOutXbox();
        ShowConsole();
    }

    private void OnLinkPs5(object sender, RoutedEventArgs e)
    {
        controller.LinkPs5Account();
        Leave();
    }

    private void OnFrameSource(object sender, SelectionChangedEventArgs e)
    {
        if (loading || FrameSourceChoice.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (controller.IsXbox)
            Update(s => s with { XboxFrameSource = tag == "CaptureWindow" ? XboxFrameSource.CaptureWindow : XboxFrameSource.Xbox });
        else
            Update(s => s with { FrameSource = Enum.Parse<FrameSource>(tag) });
        ShowConsole();
    }

    private void OnXboxLegacyFrameRate(object sender, RoutedEventArgs e) =>
        Update(s => s with { XboxLegacyFrameRate = XboxLegacyFrameRate.IsChecked == true });

    private void OnXboxAudioOnComputer(object sender, RoutedEventArgs e) =>
        Update(s => s with { XboxAudioOnComputer = XboxAudioOnComputer.IsChecked == true });

    private void OnRemotePlayApp(object sender, SelectionChangedEventArgs e)
    {
        if (RemotePlayAppChoice.SelectedItem is ComboBoxItem { Tag: string tag })
            Update(s => s with { RemotePlayApp = Enum.Parse<RemotePlayApp>(tag) });
    }

    private bool refreshingCaptureWindows;

    private void RefreshCaptureWindows()
    {
        if (refreshingCaptureWindows) return;
        refreshingCaptureWindows = true;
        try
        {
            var saved = controller.Settings.CaptureWindow;
            var windows = controller.AvailableCaptureWindows();
            CaptureWindowChoice.Items.Clear();
            ComboBoxItem? selected = null;
            foreach (var target in windows)
            {
                var option = new ComboBoxItem { Content = $"{target.Title} ({target.ProcessName})", Tag = target };
                CaptureWindowChoice.Items.Add(option);
                if (target == saved) selected = option;
            }
            if (saved is not null && selected is null)
            {
                selected = new ComboBoxItem { Content = $"{saved.Title} ({saved.ProcessName}) — {Localizer.Current["Not currently open"]}", Tag = saved };
                CaptureWindowChoice.Items.Insert(0, selected);
            }
            CaptureWindowChoice.SelectedItem = selected;
            // Görünen liste: her pencere bir seçenek kartı (ad kalın, altında uygulama); seçili olan amber çerçeveli.
            CaptureWindowCards.Children.Clear();
            foreach (ComboBoxItem item in CaptureWindowChoice.Items)
            {
                if (item.Tag is not WindowCaptureTarget target) continue;
                var card = new Controls.OptionCard
                {
                    Content = target.Title, Description = item == selected && !windows.Contains(target)
                        ? $"{target.ProcessName} · {Localizer.Current["Not currently open"]}" : target.ProcessName,
                    GroupName = "captureWindow", IsChecked = item == selected, Margin = Sp.T("0", "0", "0", "xs"), MinHeight = 64,
                };
                card.Checked += (_, _) => { if (!refreshingCaptureWindows) CaptureWindowChoice.SelectedItem = item; };
                CaptureWindowCards.Children.Add(card);
            }
            CaptureWindowEmpty.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSourceProjectorHint(saved);
        }
        finally { refreshingCaptureWindows = false; }
    }

    private void OnRefreshCaptureWindows(object sender, RoutedEventArgs e) => RefreshCaptureWindows();

    private void OnCaptureWindowChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || refreshingCaptureWindows) return;
        if (CaptureWindowChoice.SelectedItem is ComboBoxItem { Tag: WindowCaptureTarget target })
        {
            Update(s => s with { CaptureWindow = target });
            UpdateSourceProjectorHint(target);
        }
    }

    /// <summary>OBS'in Program/Önizleme projektörü seçiliyse Kaynak projektörü önerilir (görüntüde CouchSubs'ın katmanı da var).</summary>
    private void UpdateSourceProjectorHint(WindowCaptureTarget? target) =>
        SourceProjectorHint.Visibility = ObsProjector.NeedsSourceProjectorHint(target) ? Visibility.Visible : Visibility.Collapsed;

    private void OnAppearance(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
            Update(s => s with { Appearance = Enum.Parse<AppAppearance>(tag) });
    }

    private void OnControllerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppController.TvStatusText) or nameof(AppController.TvClients) or nameof(AppController.TvReconnecting))
            Dispatcher.BeginInvoke(ShowTvStatus);
        // Menü arkasındaki renk geçişi çeviri sürerken durur (işlemci oyuna kalsın).
        if (e.PropertyName == nameof(AppController.IsRunning)) Dispatcher.BeginInvoke(() => Glow.Paused = controller.IsRunning);
        if (e.PropertyName is nameof(AppController.ObsAddress) or nameof(AppController.NetworkObsAddress) or nameof(AppController.LegacyTvWarning))
        {
            ObsAddress.Text = controller.ObsAddress;
            NetworkObsAddress.Text = controller.NetworkObsAddress ?? "";
            LegacyTvWarning.Visibility = controller.LegacyTvWarning ? Visibility.Visible : Visibility.Collapsed;
        }
        if (e.PropertyName is nameof(AppController.Settings) or nameof(AppController.ShowsOnTv) or nameof(AppController.ShowsOnComputer))
            ShowStyleTitle();
        if (e.PropertyName == nameof(AppController.InstalledPacks)) ShowPacks();
        if (e.PropertyName is nameof(AppController.Settings) or nameof(AppController.AdvancedOcrInstalled) or nameof(AppController.AdvancedOcrDownloading)
            or nameof(AppController.AdvancedOcrProgress) or nameof(AppController.AdvancedOcrError))
            ShowAdvancedOcr();
        if (e.PropertyName == nameof(AppController.Settings)) ShowShortcutTexts();
        if (e.PropertyName is nameof(AppController.AreaProfileGame) or nameof(AppController.AreaProfileUnsaved)
            or nameof(AppController.AreaProfileTarget) or nameof(AppController.OfferedAreaProfileGame))
            ShowAreaProfile();
        if (e.PropertyName is nameof(AppController.GameProfiles) or nameof(AppController.AreaProfileGame)) ShowGameProfiles();
        // Oyun değişince alanlar profilden gelir ya da genel alanlara dönülür; özet yenilenir.
        if (e.PropertyName is nameof(AppController.AreaProfileGame) or nameof(AppController.Settings)) ShowAreasSummary();
    }

    /// <summary>
    /// "Bu oyunun alanları": profil etkinse hangi oyunun alanları olduğu (kaydedilmemiş değişiklik varsa "Profile kaydet");
    /// değilse oynanan oyuna kaydetme; oyun bilinmiyorsa nasıl kaydedileceği.
    /// </summary>
    private void ShowAreaProfile()
    {
        var text = Localizer.Current;
        var active = controller.AreaProfileGame;
        var target = controller.AreaProfileTarget;
        var unsaved = controller.AreaProfileUnsaved;
        AreaProfileText.Text = active is not null ? text.Format("These areas are saved for %@.", active)
            : target is not null ? text["These are your general areas."]
            : text["Areas can be saved for a game while translating, once the game being played is known."];
        AreaProfileHint.Text = active is not null
            ? text[unsaved ? "Your changes are not saved yet; they are dropped when translating stops." : "They come back by themselves whenever you play this game."]
            : target is not null ? text.Format("Save them for %@ and they come back by themselves the next time you play it.", target)
            : text["The played game is shown on the home screen; you can choose it there."];
        // Oynanan oyunun kayıtlı alanları uygulanmadıysa üzerine yazılmaz: düğme o alanları kullanır.
        var other = controller.AreaProfileTargetHasOther;
        AreaProfileSave.Content = other && target is not null ? text.Format("Use the saved areas for %@", target)
            : active is not null ? text["Save to profile"] : target is not null ? text.Format("Save for %@", target) : "";
        AreaProfileSave.Visibility = target is not null && (other || active is null || unsaved) ? Visibility.Visible : Visibility.Collapsed;
        AreaProfileDelete.Visibility = active is not null ? Visibility.Visible : Visibility.Collapsed;
        AreaProfileButtons.Visibility = AreaProfileSave.Visibility == Visibility.Visible || active is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveAreaProfile(object sender, RoutedEventArgs e)
    {
        if (controller.AreaProfileTargetHasOther
            && AreaProfiles.Key(controller.AreaProfileTarget, controller.Settings.AreaSource) is { } key)
            controller.UseAreaProfile(key);
        else
            controller.SaveAreaProfile();
    }

    // --- Oyun profilleri: listede olmayan oyun adı yazılarak eklenir; seç, yeniden adlandır, çoğalt, sil ---

    /// <summary>Ad yazılan işlem: yeni profil (null anahtar), yeniden adlandırma ya da çoğaltma.</summary>
    private (string? Key, bool Duplicate)? profileEditing;

    private void ShowGameProfiles()
    {
        var text = Localizer.Current;
        GameProfileList.Children.Clear();
        var active = controller.Settings.ActiveAreaProfile;
        foreach (var profile in controller.GameProfiles)
        {
            var row = new DockPanel { Margin = Sp.T("0", "xxs", "0", "xxs"), LastChildFill = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            void AddButton(string title, Action run, string left = "xs")
            {
                var button = new Button { Content = text[title], Padding = Sp.T("s", "0", "s", "0"), Margin = Sp.T(left, "0", "0", "0") };
                button.Click += (_, _) => run();
                buttons.Children.Add(button);
            }
            var key = profile.Key;
            var game = profile.Game;
            if (key != active) AddButton("Use", () => controller.UseAreaProfile(key), "0");
            AddButton("Rename", () => BeginProfileEdit((key, false), game));
            AddButton("Duplicate", () => BeginProfileEdit((key, true), ""));
            AddButton("Delete profile", () =>
            {
                if (MessageBox.Show(Host, text.Format("Delete the saved areas for %@?", game), text["Delete profile"], MessageBoxButton.YesNo,
                                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                    controller.DeleteAreaProfile(key);
            });
            row.Children.Add(buttons);
            row.Children.Add(new TextBlock
            {
                Text = key == active ? game + " · " + text["In use"] : game,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            GameProfileList.Children.Add(row);
        }
        GameProfileList.Visibility = GameProfileList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BeginProfileEdit((string? Key, bool Duplicate) editing, string name)
    {
        var text = Localizer.Current;
        profileEditing = editing;
        GameProfileName.Text = name;
        GameProfileProblem.Visibility = Visibility.Collapsed;
        GameProfileCommit.Content = text[editing.Key is null ? "Save current areas" : editing.Duplicate ? "Duplicate" : "Rename"];
        GameProfileEditor.Visibility = Visibility.Visible;
        GameProfileNew.Visibility = Visibility.Collapsed;
        GameProfileName.Focus();
        GameProfileName.SelectAll();
    }

    private void EndProfileEdit()
    {
        profileEditing = null;
        GameProfileEditor.Visibility = Visibility.Collapsed;
        GameProfileNew.Visibility = Visibility.Visible;
    }

    private void OnNewGameProfile(object sender, RoutedEventArgs e) => BeginProfileEdit((null, false), controller.PlayedGame ?? "");

    private void OnCancelGameProfile(object sender, RoutedEventArgs e) => EndProfileEdit();

    private void OnGameProfileNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnCommitGameProfile(sender, e);
        else if (e.Key == Key.Escape) EndProfileEdit();
    }

    private void OnCommitGameProfile(object sender, RoutedEventArgs e)
    {
        if (profileEditing is not { } editing) return;
        var text = Localizer.Current;
        var name = GameProfileName.Text.Trim();
        var edit = editing.Key is not { } key ? controller.SaveAreaProfileAs(name)
            : editing.Duplicate ? controller.DuplicateAreaProfile(key, name)
            : controller.RenameAreaProfile(key, name);
        switch (edit)
        {
            case AreaProfiles.Edit.Done:
                EndProfileEdit();
                ShowGameProfiles();
                break;
            case AreaProfiles.Edit.Exists:
                GameProfileProblem.Text = text.Format("There is already a profile for %@.", name);
                GameProfileProblem.Visibility = Visibility.Visible;
                break;
            default:
                GameProfileProblem.Text = text["Type the game's name."];
                GameProfileProblem.Visibility = Visibility.Visible;
                break;
        }
    }

    private void OnDeleteAreaProfile(object sender, RoutedEventArgs e)
    {
        if (controller.AreaProfileGame is not { } game) return;
        var text = Localizer.Current;
        if (MessageBox.Show(Host, text.Format("Delete the saved areas for %@?", game), text["Delete profile"], MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes)
            controller.DeleteAreaProfile();
    }

    private async void OnOpenTvPairing(object sender, RoutedEventArgs e)
    {
        try { await controller.OpenTvPairingAsync(); }
        catch (Exception error) { MessageBox.Show(Host, error.Message, "Çevirgen"); }
    }

    private void OnRenewAccessKey(object sender, RoutedEventArgs e) => controller.RotateSubtitleKey();

    /// <summary>Altyazı bilgisayar ekranında da gösteriliyorsa görünüm kartı yalnız TV'ye ait değildir.</summary>
    private void ShowStyleTitle()
    {
        // Başlıksız kart (Mac AppearanceCard); TV ya da OBS yokken yalnız bilgisayar ekranı yer düğmesi kalır.
        // Görüntü modu yalnız Android TV uygulamasında var.
        DisplayModePanel.Visibility = controller.ShowsOnTv && controller.Tv?.Brand == TvBrand.AndroidTv
            ? Visibility.Visible : Visibility.Collapsed;
        ComputerPlacementPanel.Visibility = controller.ShowsOnComputer ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAdjustOverlay(object sender, RoutedEventArgs e) => controller.OverlayAdjusting = true;
    /// <summary>
    /// Kurulu oyun paketleri: iki sütunlu karolar (kısaltma rozeti, ad, satır sayısı) ve üstte "Ücretsiz · n oyun" hapı.
    /// Masaüstünde paketler ücretsizdir.
    /// </summary>
    private void ShowPacks()
    {
        var text = Localizer.Current;
        PackList.Items.Clear();
        // Oyunun "(ek satırlar)" paketi asıl paketin karosunda (#353).
        var packs = GameCatalog.PackShelf(controller.InstalledPacks);
        PacksPill.Visibility = packs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PacksPill.Text = text.Format("Free · %lld games", packs.Count);
        if (UpdatePacksProgress.Visibility != Visibility.Visible) UpdatePacks.IsEnabled = controller.CanUpdatePacks;
        if (packs.Count == 0)
        {
            PackList.Items.Add(new TextBlock
            {
                Text = text["No game pack is installed."], Style = (Style)FindResource("HintText"), TextWrapping = TextWrapping.Wrap,
                Margin = Sp.T("xs"),
            });
            return;
        }
        foreach (var pack in packs) PackList.Items.Add(PackTile(pack, text.Format("%@ lines", pack.Entries.ToString("N0"))));
    }

    /// <summary>
    /// Paket karosu (Perde, Mac PackTile): 120 genişlikte 3:4 kapak (kullanıcının seçtiği görsel ya da adından türeyen
    /// degrade, alttan scrim üstünde kalın beyaz ad); oynanan oyunun paketi "Oynanıyor" cam hapı + amber halka + kapak
    /// ışıması; altında yeşil nokta ve mono satır sayısı. Kapağa tıklayınca görsel seçilir, sağ tıkta seç/kaldır.
    /// </summary>
    private FrameworkElement PackTile(PackShelfItem pack, string lines)
    {
        var text = Localizer.Current;
        const double width = 120;
        var playing = controller.PlayedGame is { } game && pack.Titles.Append(pack.Title).Any(title => GameName.Key(title) == GameName.Key(game));
        var cover = new Controls.GameCover(pack.Title, playing ? text["Playing"] : null, playing, width)
        {
            ToolTip = CoverText("Choose game cover…", "Oyun kapağı seç…"),
        };
        var coverPath = GameCoverStore.PathFor(pack.Id);
        if (coverPath is not null && File.Exists(coverPath))
        {
            try
            {
                // OnLoad releases the file before the image is displayed, so replacing a cover works immediately.
                using var stream = File.OpenRead(coverPath);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 256;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                // Seçilen görsel kapağın degradesinin yerine (scrim ve ad üstünde kalır).
                if (FindCoverArt(cover) is { } art && art.Parent is Grid stage)
                {
                    var index = stage.Children.IndexOf(art);
                    stage.Children.Insert(index + 1, new Image { Source = image, Stretch = Stretch.UniformToFill });
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or NotSupportedException)
            {
                // A corrupted optional cover must never hide the pack or prevent opening Settings.
            }
        }
        System.Windows.Automation.AutomationProperties.SetName(cover, CoverText("Choose game cover…", "Oyun kapağı seç…") + " " + pack.Title);
        cover.Click += (_, _) => ChoosePackCover(pack.Id);
        var choose = new MenuItem { Header = CoverText("Choose game cover…", "Oyun kapağı seç…") };
        choose.Click += (_, _) => ChoosePackCover(pack.Id);
        var remove = new MenuItem
        {
            Header = CoverText("Remove game cover", "Oyun kapağını kaldır"), IsEnabled = coverPath is not null && File.Exists(coverPath),
        };
        remove.Click += (_, _) =>
        {
            try { GameCoverStore.Remove(pack.Id); ShowPacks(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(Host, CoverText("The game cover could not be saved.", "Oyun kapağı kaydedilemedi."), text["Game packs"],
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        cover.ContextMenu = new ContextMenu { Items = { choose, remove } };

        var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("0", "0", "xxs", "0") };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "successBrush");
        var count = new TextBlock { Text = lines, FontSize = 11, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        System.Windows.Documents.Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);
        count.SetResourceReference(TextBlock.ForegroundProperty, "mutedBrush");
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = Sp.T("0", "xs", "0", "0") };
        meta.Children.Add(dot);
        meta.Children.Add(count);
        var tile = new StackPanel { Width = width, Margin = Sp.T("0", "s", "m", "s") };
        tile.Children.Add(cover);
        tile.Children.Add(meta);
        return tile;
    }

    /// <summary>Kapak kartının içindeki degrade katmanı (seçilen görsel onun hemen üstüne konur).</summary>
    private static Controls.CoverArt? FindCoverArt(DependencyObject root)
    {
        // Görsel ağaç henüz kurulmadığından mantıksal ağaçta aranır; bulanık ışıma kopyası (Border içinde) atlanır.
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Controls.CoverArt art && art.Parent is Grid) return art;
            if (FindCoverArt(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Import and center-crop one cover to a 128px PNG; no network or image dependency is needed.</summary>
    private void ChoosePackCover(string packId)
    {
        var text = Localizer.Current;
        if (GameCoverStore.PathFor(packId) is null) return;
        var picker = new OpenFileDialog
        {
            Title = CoverText("Choose game cover…", "Oyun kapağı seç…"), CheckFileExists = true,
            Filter = $"{CoverText("Images", "Görseller")} (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
        };
        if (picker.ShowDialog(Host) != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Image exceeds 8 MB.");
            using var source = File.OpenRead(picker.FileName);
            // Decode a bounded preview instead of expanding a potentially huge original into RAM.
            var frame = new BitmapImage();
            frame.BeginInit();
            frame.CacheOption = BitmapCacheOption.OnLoad;
            frame.DecodePixelWidth = 512;
            frame.StreamSource = source;
            frame.EndInit();
            frame.Freeze();
            if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096)
                throw new InvalidDataException("Image dimensions are too large.");
            var side = Math.Min(frame.PixelWidth, frame.PixelHeight);
            if (side == 0) throw new InvalidDataException("Empty image.");
            var crop = new CroppedBitmap(frame,
                new Int32Rect((frame.PixelWidth - side) / 2, (frame.PixelHeight - side) / 2, side, side));
            var thumb = new TransformedBitmap(crop, new ScaleTransform(128.0 / side, 128.0 / side));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(thumb));
            using var output = new MemoryStream();
            encoder.Save(output);
            GameCoverStore.Save(packId, output.ToArray());
            ShowPacks();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException or FormatException or NotSupportedException)
        {
            MessageBox.Show(Host, CoverText("The game cover could not be saved.", "Oyun kapağı kaydedilemedi."), text["Game packs"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Kapak metinleri artık Mac ile ortak katalogda (Localizable.xcstrings); Türkçe parametre yalnız okunurluk için kalır.
    private static string CoverText(string english, string turkish) => Localizer.Current[english];

    /// <summary>"Grand Theft Auto V" → "GTA": ilk üç sözcüğün baş harfleri (rakam olduğu gibi).</summary>
    internal static string Initials(string title)
    {
        var words = title.Split([' ', ':', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        var letters = words.Where(word => char.IsLetterOrDigit(word[0])).Take(3).Select(word => char.ToUpperInvariant(word[0]));
        return string.Concat(letters);
    }

    /// <summary>
    /// "Paketleri şimdi güncelle": beklerken düğme "Güncelleniyor…" olur; sonuç ("Paketler zaten güncel." ya da neyin
    /// geldiği) iletide gösterilir, hatada "Yeniden dene" sorulur.
    /// </summary>
    private async void OnUpdatePacks(object sender, RoutedEventArgs e)
    {
        var text = Localizer.Current;
        while (true)
        {
            UpdatePacks.IsEnabled = false;
            UpdatePacks.Content = text["Updating…"];
            UpdatePacksProgress.Visibility = UpdatePacksStatus.Visibility = Visibility.Visible;
            PackUpdateOutcome? outcome;
            try
            {
                outcome = await controller.UpdatePacksNowAsync();
            }
            finally
            {
                UpdatePacks.Content = text["Update the packs now"];
                UpdatePacks.IsEnabled = controller.CanUpdatePacks;
                UpdatePacksProgress.Visibility = UpdatePacksStatus.Visibility = Visibility.Collapsed;
            }
            if (outcome is null || !IsLoaded) return;
            if (outcome.Result != PackUpdateOutcome.Kind.Failed)
            {
                MessageBox.Show(Host, outcome.Message(text), outcome.Title(text), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var again = MessageBox.Show(Host, outcome.Message(text) + "\n\n" + text["Try again"] + "?", outcome.Title(text),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (again != MessageBoxResult.Yes) return;
        }
    }

    private async void OnImportPack(object sender, RoutedEventArgs e)
    {
        var text = Localizer.Current;
        var picker = new OpenFileDialog
        {
            Title = text["Import game pack"],
            Filter = $"{text["Translation files"]} (*.json, *.txt, *.srt)|*.json;*.txt;*.srt",
        };
        if (picker.ShowDialog(Host) != true) return;

        var sourcePath = picker.FileName;
        ImportPack.IsEnabled = false;
        GamePackImportPreview preview;
        try
        {
            preview = await Task.Run(() =>
            {
                if (new FileInfo(sourcePath).Length > GamePackImporter.MaxFileBytes) throw new InvalidDataException("The file is too large.");
                return GamePackImporter.Parse(File.ReadAllBytes(sourcePath), Path.GetFileName(sourcePath));
            });
        }
        catch (InvalidDataException)
        {
            if (IsLoaded)
                MessageBox.Show(Host, text["The translation file could not be imported. Check its format and encoding."],
                    picker.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (IsLoaded)
                MessageBox.Show(Host, text["The translation file could not be read."], picker.Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            ImportPack.IsEnabled = true;
        }

        if (IsLoaded && new GamePackImportWindow(preview, sourcePath) { Owner = Host }.ShowDialog() == true)
            controller.ReloadGamePacks();
    }

    private void OnGamePacks(object sender, RoutedEventArgs e) => Update(s => s with { GamePacks = GamePacks.IsChecked == true });

    private void OnSharePool(object sender, RoutedEventArgs e) => Update(s => s with { SharePool = SharePool.IsChecked == true });

    private void OnUsageData(object sender, RoutedEventArgs e) => Update(s => s with { UsageData = UsageData.IsChecked == true });

    private void OnAskGame(object sender, RoutedEventArgs e) => Update(s => s with { AskGame = AskGame.IsChecked == true });

    private void OnAutoScreenText(object sender, RoutedEventArgs e)
    {
        // Türler yalnız ayar açıkken görünür.
        ScreenTextKindsPanel.Visibility = AutoScreenText.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Update(s => s with { AutoScreenText = AutoScreenText.IsChecked == true });
    }

    /// <summary>Otomatik ekran yazısının türleri (bkz. <see cref="ScreenTextKind"/>).</summary>
    private void OnScreenTextKind(object sender, RoutedEventArgs e) => Update(s => s with
    {
        ScreenTextPhone = ScreenTextPhone.IsChecked == true,
        ScreenTextMission = ScreenTextMission.IsChecked == true,
        ScreenTextHelp = ScreenTextHelp.IsChecked == true,
        ScreenTextMenus = ScreenTextMenus.IsChecked == true,
        ScreenTextOther = ScreenTextOther.IsChecked == true,
    });

    private void OnSetTextApart(object sender, RoutedEventArgs e) => Update(s => s with { SetTextApart = SetTextApart.IsChecked == true });

    private void OnGameAreaPresets(object sender, RoutedEventArgs e) => Update(s => s with { GameAreaPresets = GameAreaPresets.IsChecked == true });

    /// <summary>Ayarlar › Konsol › TV girişi: seçilen giriş kaydedilir ve TV'de deneme altyazısı gösterilir (Mac ile aynı).</summary>
    private void OnHdmiInput(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var input)) return;
        if (controller.Settings.Style.HdmiInput == input) return;
        controller.TryHdmiInput(input, confirmed: true);
        controller.SendTestSubtitle();
    }

    private void OnOcrEngine(object sender, SelectionChangedEventArgs e)
    {
        if (OcrEngineChoice.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var engine = Enum.Parse<OcrEngine>(tag);
        if (controller.Settings.OcrEngine == engine) return;
        Update(s => s with { OcrEngine = engine });
        // İlk seçişte model kendiliğinden iner; inene kadar Windows'un yazı tanıması okur.
        if (engine == OcrEngine.Advanced && !controller.AdvancedOcrInstalled) _ = controller.DownloadAdvancedOcrAsync();
        ShowAdvancedOcr();
    }

    private async void OnDownloadAdvancedOcr(object sender, RoutedEventArgs e) => await controller.DownloadAdvancedOcrAsync();

    /// <summary>Gelişmiş yazı tanıma seçiliyse modelin durumu: indirme düğmesi, ilerleme, hata.</summary>
    private void ShowAdvancedOcr()
    {
        OcrEnginePanel.Visibility = controller.AdvancedOcrAvailable ? Visibility.Visible : Visibility.Collapsed;
        var advanced = controller.Settings.OcrEngine == OcrEngine.Advanced;
        AdvancedOcrDownload.Visibility = advanced && !controller.AdvancedOcrInstalled ? Visibility.Visible : Visibility.Collapsed;
        AdvancedOcrDownloadButton.IsEnabled = !controller.AdvancedOcrDownloading;
        AdvancedOcrProgress.Visibility = AdvancedOcrProgressText.Visibility =
            controller.AdvancedOcrDownloading ? Visibility.Visible : Visibility.Collapsed;
        AdvancedOcrProgress.Value = controller.AdvancedOcrProgress;
        AdvancedOcrProgressText.Text = Localizer.Current["Downloading the model… %lld%%"]
            .Replace("%lld", ((int)(controller.AdvancedOcrProgress * 100)).ToString()).Replace("%%", "%");
        AdvancedOcrError.Text = controller.AdvancedOcrError ?? "";
        AdvancedOcrError.Visibility = advanced && controller.AdvancedOcrError is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTranslationDevice(object sender, SelectionChangedEventArgs e)
    {
        ShowCpuThreads();
        if (TranslationDeviceChoice.SelectedItem is ComboBoxItem { Tag: string tag })
            Update(s => s with { TranslationDevice = Enum.Parse<TranslationDevice>(tag) });
    }

    private void OnCpuThreads(object sender, SelectionChangedEventArgs e)
    {
        ShowCpuThreads();
        if (CpuThreadChoice.SelectedItem is ComboBoxItem { Tag: int count })
            Update(s => s with { CpuTranslationThreads = count });
    }

    private void ShowCpuThreads()
    {
        if (CpuThreadPanel is null || CpuThreadWarning is null) return;
        CpuThreadPanel.Visibility = TranslationDeviceChoice.SelectedItem is ComboBoxItem { Tag: "Cpu" }
            ? Visibility.Visible : Visibility.Collapsed;
        CpuThreadWarning.Visibility = CpuThreadChoice?.SelectedItem is ComboBoxItem { Tag: int count }
                                      && CpuThreadOptions.NearCapacity(count, Environment.ProcessorCount)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFastSubtitleDetection(object sender, RoutedEventArgs e) =>
        Update(s => s with { FastSubtitleDetection = FastSubtitleDetection.IsChecked == true });

    private void OnAutoReadGamePhone(object sender, RoutedEventArgs e) =>
        Update(s => s with { AutoReadGamePhone = AutoReadGamePhone.IsChecked == true });

    private void OnPhoneMessages(object sender, RoutedEventArgs e) =>
        Update(s => s with { PhoneMessagesPanel = PhoneMessagesPanel.IsChecked == true });

    /// <summary>Ayarların sahibi olan pencere (ileti kutuları ve dosya seçicileri bunun üstünde açılır).</summary>
    private Window? Host => Window.GetWindow(this) ?? Application.Current?.MainWindow;

    /// <summary>Ayarlardan bir işe geçilince (Xbox bağla, yeniden kur …) ana ekrana dönülür; eskiden ayar penceresi kapanırdı.</summary>
    private void Leave() => ShellWindow.Navigate(ShellTab.Home);

    /// <summary>Ayarlar sekmesini açar.</summary>
    public static void Open(Window? owner, AppController controller) => ShellWindow.Navigate(ShellTab.Settings);

    /// <summary>Ayarları belli bir bölümde açar (ana ekrandaki bilgi karosunun "Çeviri" satırı).</summary>
    public static void Open(Window? owner, AppController controller, SettingsSection section) => ShellWindow.Navigate(ShellTab.Settings, section);

    /// <summary>Eski bölüm adının Ayarlar'daki bölümü (Görünüm ve Çeviri kendi sekmelerinde; kabuk onları sekmeye yönlendirir).</summary>
    public static SettingsGroup GroupFor(SettingsSection section) => section switch
    {
        SettingsSection.PictureSource => SettingsGroup.Devices,
        _ => SettingsGroup.Advanced,
    };

    /// <summary>Kabuk Ayarlar sekmesini açınca istenen bölüm seçilir.</summary>
    internal void Show(SettingsSection? section)
    {
        if (section is { } wanted) Show(GroupFor(wanted));
    }

    /// <summary>Bölümü açar (soldaki listede de seçilir).</summary>
    public void Show(SettingsGroup group) => Sections.SelectedIndex = (int)group;

    /// <summary>
    /// "Kurulumu baştan başlat" (üst çubuk ve Uygulama › Hakkında): onaydan sonra kayıtlı PS5, hesaplar ve TV eşleşmesi silinir,
    /// sihirbaz platform sorusundan açılır.
    /// </summary>
    internal static void ConfirmReset(Window? owner, AppController controller)
    {
        var text = Localizer.Current;
        var answer = owner is null
            ? MessageBox.Show(text["The saved PS5, account and TV connection will be removed."], text["Set up again?"], MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            : MessageBox.Show(owner, text["The saved PS5, account and TV connection will be removed."], text["Set up again?"], MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        controller.ResetSetup();
    }

    private void Update(Func<UserSettings, UserSettings> change)
    {
        if (loading) return;
        controller.Settings = change(controller.Settings);
    }

    // ───── Yeni arayüz: gezinme, Temel sayfası ─────

    /// <summary>Soldaki gezinme satırı seçilince o sayfa açılır.</summary>
    /// <summary>Menüdeki sırasıyla bölüm açar (0 Cihazlar … 5 Uygulama); ekran görüntüsü aracı için.</summary>
    public void ShowPage(int index) => Sections.SelectedIndex = index;

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var index) && Sections.SelectedIndex != index)
            Sections.SelectedIndex = index;
    }

    /// <summary>Sayfa başka yoldan (ana ekrandaki satır, "Altyazı Görünümünü Düzenle") açılınca gezinme de seçilir.</summary>
    private void OnSection(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Sections) return;
        foreach (var item in Nav.Children.OfType<Controls.NavItem>())
            item.IsChecked = item.Tag is string tag && tag == Sections.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MoveSelection(true);
        ShowHeading();
        if (Sections.SelectedContent is FrameworkElement page) Theme.Motion.EnterFrom(page, 8, Theme.Motion.Medium);
    }

    /// <summary>
    /// Seçim hapını seçili menü satırına taşır (Mac matchedGeometryEffect): konum ve yükseklik giriş eğrisiyle orta sürede
    /// kayar; hareket azaltılmışsa ya da Geçiş Animasyonları kapalıysa kısa sürede.
    /// </summary>
    private void MoveSelection(bool animate)
    {
        var item = Nav.Children.OfType<Controls.NavItem>().FirstOrDefault(i => i.IsChecked == true);
        if (item is null || !item.IsLoaded || item.ActualHeight <= 0)
        {
            Selection.Visibility = Visibility.Hidden;
            return;
        }
        var top = item.TranslatePoint(new Point(0, 0), NavHost).Y;
        var height = item.ActualHeight;
        var wasHidden = Selection.Visibility != Visibility.Visible;
        Selection.Visibility = Visibility.Visible;
        if (!animate || wasHidden)
        {
            SelectionOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            Selection.BeginAnimation(HeightProperty, null);
            SelectionOffset.Y = top;
            Selection.Height = height;
            return;
        }
        SelectionOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, Theme.Motion.To(top, Theme.Motion.Medium));
        Selection.BeginAnimation(HeightProperty, Theme.Motion.To(height, Theme.Motion.Medium));
    }


    private void OnStartupSound(object sender, RoutedEventArgs e)
    {
        Update(s => s with { StartupSound = StartupSound.IsChecked == true });
    }

    private void OnTransitionAnimations(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        var on = TransitionAnimations.IsChecked == true;
        Update(s => s with { TransitionAnimations = on });
        Theme.Motion.AnimationsEnabled = on;
    }

    /// <summary>Deneysel ayarlar kapanır (ekran yazısı ve GTA 6 telefonu).</summary>
    private void OnExperimentalDefaults(object sender, RoutedEventArgs e)
    {
        AutoScreenText.IsChecked = false;
        AutoReadGamePhone.IsChecked = false;
    }

    /// <summary>TV durumu noktası (tek renk modeli): bağlı ya da hazır yeşil, bekleniyor sarı-turuncu, kurulmamış gri.</summary>
    private void ShowTvStatus()
    {
        TvStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, DeviceStatus.Brush(controller.TvState));
        TvCardHost.Content = TvCard(controller.Tv, controller.HasTv, controller.TvClients > 0);
    }

    /// <summary>
    /// TV kartı (Mac DSTVCard, eylemsiz): 36'lık simge karesi, ad + mono "Marka · adres", sağda durum hapı. Her zaman kıl çizgi;
    /// TV uygulaması bağlıyken 2 px amber kenar + amber yumuşak dolgu + ışıma ve amber simge.
    /// </summary>
    private static FrameworkElement TvCard(CouchSubs.Core.LgTv.LgTv? tv, bool linked, bool connected)
    {
        var text = Localizer.Current;
        var brand = tv?.Brand switch { CouchSubs.Core.LgTv.TvBrand.Samsung => "Samsung", CouchSubs.Core.LgTv.TvBrand.AndroidTv => "Android TV", _ => "LG" };
        var name = new TextBlock { Text = tv is null ? "TV" : tv.Name.Length == 0 ? brand : tv.Name, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        name.SetResourceReference(TextBlock.FontSizeProperty, "bodyStrongFontSize");
        name.SetResourceReference(TextBlock.ForegroundProperty, "textBrush");
        var detail = new TextBlock { Text = tv is null ? "" : $"{brand} · {tv.Host}", FontSize = 11, FontWeight = FontWeights.Medium, Margin = Sp.T("0", "line", "0", "0"),
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        System.Windows.Documents.Typography.SetNumeralAlignment(detail, FontNumeralAlignment.Tabular);
        detail.SetResourceReference(TextBlock.ForegroundProperty, "mutedBrush");
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("s", "0", "s", "0") };
        names.Children.Add(name);
        if (tv is not null) names.Children.Add(detail);
        var row = new DockPanel();
        var square = new Controls.IconSquare { Glyph = "", Tone = connected ? Controls.IconTone.Amber : Controls.IconTone.Gray, Size = 36 };
        DockPanel.SetDock(square, Dock.Left);
        row.Children.Add(square);
        row.Children.Add(names);
        // Durum (nokta + yazı) kartın altındaki satırda; kart sade: kıl çizgi, köşe 6, ışıma yok. Bağlıyken sarı kenar.
        var card = new Border { CornerRadius = new CornerRadius(6), Padding = Sp.T("s"), MinHeight = 64, BorderThickness = new Thickness(1), Child = row };
        card.SetResourceReference(Border.BackgroundProperty, "surfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, connected ? "amberBrush" : "borderBrush");
        return card;
    }

    /// <summary>Bölümlerin başlığı (Mac SectionHeading): simge, ton, ad ve iki kelimelik alt yazı; menü sırasıyla.</summary>
    private static readonly string[] Headings = ["Cihazlar", "Okuma alanları", "Seslendirme", "Kısayollar", "Gelişmiş", "Uygulama"];

    /// <summary>
    /// Sağ sütunun başındaki bölüm başlığı (Mac SectionHeading): 40'lık renkli simge karesi + başlık (24) + iki kelimelik alt
    /// yazı. Kayan içeriğin içinde durur (cam üst çubuğun arkasından geçer).
    /// </summary>
    private void ShowHeading()
    {
        var index = Math.Clamp(Sections.SelectedIndex, 0, Headings.Length - 1);
        var heading = new TextBlock { Text = Headings[index], Margin = Sp.T("0", "0", "0", "l") };
        heading.SetResourceReference(StyleProperty, "ScreenTitle");
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(heading, System.Windows.Automation.AutomationHeadingLevel.Level1);
        Sections.Tag = heading;
    }

    /// <summary>Menüde amber simge karesinin simgesi koyu (Perde: amberde koyu, diğer tonlarda beyaz).</summary>
    private void DarkenAmberGlyph()
    {
        foreach (var item in Nav.Children.OfType<Controls.NavItem>().Where(item => item.Tag is "0"))
        {
            item.ApplyTemplate();
            if (item.Template?.FindName("Glyph", item) is TextBlock glyph) glyph.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1E, 0x22));
        }
    }


    private void OnFreeTarget(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not RadioButton { Tag: string tag }) return;
        var target = Enum.Parse<ResultTarget>(tag);
        Update(s => s with { FreeAreaTarget = target });
    }

    private void OnMisreadWait(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ShowWaits();
        Update(s => s with { MisreadWaitMs = (int)Math.Round(e.NewValue * 1000) });
    }

    private void OnPartialWait(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ShowWaits();
        Update(s => s with { PartialWaitMs = (int)Math.Round(e.NewValue * 1000) });
    }

    private void OnWaitDefaults(object sender, RoutedEventArgs e)
    {
        MisreadWait.Value = SubtitlePipeline.PackWaitMs / 1000.0;
        PartialWait.Value = SubtitlePipeline.CleanWaitMs / 1000.0;
    }

    /// <summary>Kaydırıcıların yanındaki süre ("0,8 sn").</summary>
    private void ShowWaits()
    {
        if (MisreadWaitValue is null || PartialWaitValue is null) return;
        var unit = Localizer.Current["sec"];
        MisreadWaitValue.Text = $"{MisreadWait.Value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)} {unit}";
        PartialWaitValue.Text = $"{PartialWait.Value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)} {unit}";
    }

    private void OnDisplayMode(object sender, SelectionChangedEventArgs e)
    {
        if (DisplayMode.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        Update(s => s with { Style = s.Style with { DisplayMode = Enum.Parse<TvDisplayMode>(tag) } });
    }


    /// <summary>
    /// Okunan alanların özeti: altyazı alanı ve ek alanlar (ad, sürekli / kısayolla, yüzde koordinatlar); alanların hangi
    /// oyuna ait olduğu. Düzenleme "Alanları düzenle" ekranında.
    /// </summary>
    private void ShowAreasSummary()
    {
        var model = new AreaEditModel(controller.Settings.Areas, controller.Settings.KeepAreaCentered);
        AreasIntro.Text = controller.AreaEditGame is { } game
            ? $"{game} için okunan alanlar. Kutuları son oyun karesinin üstünde taşıyıp boyutlandırmak için Alanları düzenle'yi aç."
            : "Genel alanlar (oyun bilinmiyor). Kutuları son oyun karesinin üstünde taşıyıp boyutlandırmak için Alanları düzenle'yi aç.";
        AreasSummary.Children.Clear();
        foreach (var box in model.Boxes)
        {
            var row = new DockPanel { Margin = Sp.T("0", "0", "0", "xxs") };
            var where = ScreenKit.Mono(AreaEditModel.Coordinates(box.Region), 12);
            DockPanel.SetDock(where, Dock.Right);
            row.Children.Add(where);
            var mode = box.Main ? "" : box.Live ? " · sürekli" : " · kısayolla";
            row.Children.Add(ScreenKit.Text($"{model.Number(box)} · {model.DisplayName(box)}{mode}", 14, wrap: false));
            AreasSummary.Children.Add(row);
        }
    }

    private void OnEditAreas(object sender, RoutedEventArgs e) => ShellWindow.EditAreas();

    private void OnTest(object sender, RoutedEventArgs e) => controller.SendTestSubtitle();

    private void OnAbout(object sender, RoutedEventArgs e) => AboutWindow.Open(Host);

    /// <summary>Çevirgen'in GitHub sayfası: kaynak kodu ve sürümler.</summary>
    private void OnRepository(object sender, RoutedEventArgs e) => controller.OpenInBrowser(UpdateChannel.RepositoryPage);

    /// <summary>Sorun bildir: Çevirgen'in GitHub sorun sayfası (günlükler yukarıdaki "Günlükleri kaydet" ile eklenir).</summary>
    private void OnReportIssue(object sender, RoutedEventArgs e) => controller.OpenInBrowser(UpdateChannel.IssuesPage);

    private void OnReset(object sender, RoutedEventArgs e) => ConfirmReset(Host, controller);


    private void OnStreamBackground(object sender, SelectionChangedEventArgs e)
    {
        if (StreamBackgroundChoice.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<StreamBackground>(tag, out var background))
            controller.StreamBackground = background;
    }

    /// <summary>Akış penceresi açıkken düğme onu kapatır (Mac'teki "Akış penceresini kapat" ile aynı).</summary>
    private void OnOpenStreamWindow(object sender, RoutedEventArgs e)
    {
        if (StreamWindow.IsOpen) StreamWindow.CloseIfOpen();
        else StreamWindow.Open(controller);
        ShowStreamWindowToggle();
    }

    private void ShowStreamWindowToggle() =>
        StreamWindowToggle.Content = Localizer.Current[StreamWindow.IsOpen ? "Close the stream window" : "Open the stream window"];

    /// <summary>Kısayolların geçtiği bütün metinler ve Kısayollar'daki düğmeler kullanıcının seçtiği tuşları gösterir.</summary>
    private void ShowShortcutTexts()
    {
        var settings = controller.Settings;
        ScreenCard.Title = HotkeyText.Title(settings, HotkeyAction.Screen, "Translate the screen (%@)");
        ScreenHint.Text = HotkeyText.Sentence(settings, HotkeyAction.Screen,
            "Press %@ while playing: every text on the screen is translated once and shown on the TV in its own place. Press again to remove the translations.");
        FreeAreaCard.Title = HotkeyText.Title(settings, HotkeyAction.FreeArea, "Free area (%@)");
        FreeAreaHint.Text = HotkeyText.Sentence(settings, HotkeyAction.FreeArea,
            "Press %@ while playing: the picture freezes on the PC, drag over any part of it to translate that part once. Esc closes.");
        PauseHint.Text = HotkeyText.Sentence(settings, HotkeyAction.PauseSubtitles,
            "%@ pauses or resumes the live subtitle translation, which helps when you translate areas.");
        foreach (var ((action, slot), button) in shortcutButtons)
            if (recording?.Button != button) button.Content = ShortcutLabel(HotkeyTable.KeyFor(settings.Hotkeys, action, slot));
    }

    private static string ShortcutLabel(HotkeyKey? key) => key?.Label ?? Localizer.Current["Not set"];

    /// <summary>Kısayollar: her iş bir satır, iki düğme (iki tuş).</summary>
    private void BuildShortcuts()
    {
        foreach (var action in HotkeyText.Order)
        {
            var row = ShortcutGrid.RowDefinitions.Count;
            ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock
            {
                Text = HotkeyText.Name(action),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = Sp.T("0", "0", "m", "xs"),
            };
            Grid.SetRow(name, row);
            ShortcutGrid.Children.Add(name);
            for (var slot = 0; slot < HotkeyTable.SlotCount; slot++)
            {
                var button = new Button { MinWidth = 160, Margin = new Thickness(slot == 0 ? 0 : Sp.V("xs"), 0, 0, Sp.V("xs")), Tag = (action, slot) };
                button.Click += OnShortcutClick;
                button.LostKeyboardFocus += (_, _) => { if (recording?.Button == button) StopRecording(); };
                Grid.SetRow(button, row);
                Grid.SetColumn(button, 1 + slot);
                ShortcutGrid.Children.Add(button);
                shortcutButtons[(action, slot)] = button;
            }
        }
    }

    private void OnShortcutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<HotkeyAction, int> tag } button) return;
        if (recording?.Button == button)
        {
            StopRecording();
            return;
        }
        StopRecording();
        ShowShortcutMessage(null, null);
        recording = (tag.Item1, tag.Item2, button);
        button.Content = Localizer.Current["Press the keys…"];
        // Çeviri sürerken basılan tuş kısayol olarak çalışmasın, buraya gelsin.
        controller.HotkeysPaused = true;
        button.Focus();
    }

    /// <summary>Yeni tuşu beklemeyi bırakır; düğme kayıtlı tuşu gösterir, kısayollar yeniden tutulur.</summary>
    private void StopRecording()
    {
        if (recording is not { } current) return;
        recording = null;
        current.Button.Content = ShortcutLabel(HotkeyTable.KeyFor(controller.Settings.Hotkeys, current.Action, current.Slot));
        controller.HotkeysPaused = false;
    }

    private void OnShortcutKey(object sender, KeyEventArgs e)
    {
        if (recording is not { } current) return;
        e.Handled = true;
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        // Değiştirici tek başına basıldı: asıl tuş bekleniyor.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;
        var held = Keyboard.Modifiers;
        if (held == ModifierKeys.None && key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (held == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            AssignShortcut(current, null);
            return;
        }
        var modifiers = HotkeyModifiers.None;
        if (held.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (held.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (held.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        var chosen = new HotkeyKey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        // Win tuşlu birleşimler Windows'a aittir.
        var problem = held.HasFlag(ModifierKeys.Windows) ? HotkeyProblem.Reserved : HotkeyTable.Validate(chosen);
        if (problem != HotkeyProblem.None)
        {
            ShowShortcutMessage(null, ProblemText(problem));
            return;
        }
        AssignShortcut(current, chosen);
    }

    private void AssignShortcut((HotkeyAction Action, int Slot, Button Button) target, HotkeyKey? key)
    {
        var text = Localizer.Current;
        // Kısayollar bırakılmışken denenir: alınamıyorsa tuşu başka bir uygulama tutuyordur.
        var busy = key is not null && !GlobalHotkeys.IsAvailable(new WindowInteropHelper(Host).Handle, key);
        var choices = HotkeyTable.Assign(controller.Settings.Hotkeys, target.Action, target.Slot, key, out var displaced);
        recording = null;
        controller.Settings = controller.Settings with { Hotkeys = choices };
        controller.HotkeysPaused = false;
        ShowShortcutTexts();
        var moved = displaced is { } from && key is not null ? text.Format("%@ moved here from “%@”.", key.Label, HotkeyText.Name(from)) : null;
        var taken = busy && key is not null ? text.Format("Another app is using %@. It may not work while that app is open.", key.Label) : null;
        ShowShortcutMessage(moved, taken);
    }

    private void OnShortcutDefaults(object sender, RoutedEventArgs e)
    {
        StopRecording();
        ShowShortcutMessage(null, null);
        controller.Settings = controller.Settings with { Hotkeys = [] };
    }

    private void ShowShortcutMessage(string? message, string? error)
    {
        ShortcutMessage.Text = message ?? "";
        ShortcutMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        ShortcutError.Text = error ?? "";
        ShortcutError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string ProblemText(HotkeyProblem problem) => Localizer.Current[problem switch
    {
        HotkeyProblem.NeedsModifier => "Use this key with Ctrl+Shift, Alt or Alt+Shift, so typing in other apps isn't affected.",
        HotkeyProblem.AltGr => "Ctrl+Alt is AltGr on many keyboards and would block characters like # and {. Use Ctrl+Shift instead.",
        HotkeyProblem.Reserved => "Windows already uses this shortcut.",
        _ => "This key can't be a shortcut.",
    }];
}
