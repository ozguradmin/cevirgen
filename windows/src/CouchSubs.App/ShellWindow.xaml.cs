// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using CouchSubs.App.Controls;
using CouchSubs.App.Services;
using CouchSubs.App.Theme;
using CouchSubs.App.Views;
using CouchSubs.Shell;
using CouchSubs.Core;
using Microsoft.Win32;
using Hyperlink = System.Windows.Documents.Hyperlink;
using Run = System.Windows.Documents.Run;

namespace CouchSubs.App;

/// <summary>Üst çubuktaki bölümler (taslak sırasıyla).</summary>
public enum ShellTab { Home, Appearance, Translation, History, Settings }

/// <summary>
/// Uygulamanın tek penceresi: kurulum bitmediyse sihirbaz; bittiyse güncelleme şeridi, duyuru, üst çubuk (çevirgen adı,
/// bölüm sekmeleri, cihaz durumu, başlat/durdur), seçili sayfa ve reklam bandı.
/// </summary>
public partial class ShellWindow : Window
{
    /// <summary>
    /// Sekmelerin sayfaları: Görünüm (okunurluk testi), Çeviri (kör test ve motor kurulumu), Geçmiş ve sözlük, Ayarlar.
    /// Canlı ekran (MainView) bir kez kurulur ve saklanır; diğerleri her açılışta güncel ayarlarla yeniden kurulur.
    /// Alanları düzenle sekme değildir: <see cref="EditAreas"/> pencerenin tamamını kaplar.
    /// </summary>
    internal static readonly Dictionary<ShellTab, Func<AppController, FrameworkElement>> Pages = new()
    {
        [ShellTab.Appearance] = controller => new AppearancePage(controller),
        [ShellTab.Translation] = controller => new TranslationPage(controller),
        [ShellTab.History] = controller => new HistoryPage(controller),
        [ShellTab.Settings] = controller => new SettingsView(controller),
    };

    private readonly AppController controller;
    private bool showingMain;
    private GlobalHotkeys? hotkeys;
    private ShellTab tab = ShellTab.Home;
    private MainView? home;
    private bool switching;
    private string? notesVersion;
    private static ShellWindow? current;

    public ShellWindow(AppController controller)
    {
        this.controller = controller;
        current = this;
        InitializeComponent();
        FocusCue.Apply(this);
        ApplyMinimumSize();
        SourceInitialized += (_, _) => PaintCaption();
        // Hareket kapısı: pencere önde değilken (arka planda, simge durumunda) ve çeviri sürerken sürekli animasyonlar durur.
        Activated += (_, _) => MotionGate.WindowActive = WindowState != WindowState.Minimized;
        Deactivated += (_, _) => MotionGate.WindowActive = false;
        StateChanged += (_, _) => MotionGate.WindowActive = WindowState != WindowState.Minimized && IsActive;
        MotionGate.Translating = controller.IsRunning;
        // Sayfalar artık çubukların arkasından kaymaz; eski sayfaların içeriden başlama payı sıfır.
        Root.Resources["shell.inset"] = new Thickness(0);
        // Reklam listesi sonradan gelirse bant görünür olur.
        void OnAds() => Dispatcher.BeginInvoke(ApplyAds);
        AdCatalog.Changed += OnAds;
        Closed += (_, _) => AdCatalog.Changed -= OnAds;
        ThemeManager.Changed += PaintCaption;
        controller.PropertyChanged += OnChanged;
        controller.TvPairingRequested += OnTvPairing;
        // Bilgisayar uykudan uyanınca bağlantılar hemen yenilenir.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        // Kısayollar (varsayılan Num 0, 1, 2, 3, 5, 7, 8, 9, Ctrl+Shift+rakam ve Ctrl+H; Ayarlar › Kısayollar'da değişir)
        // yalnız çeviri sürerken tutulur.
        SourceInitialized += (_, _) =>
        {
            hotkeys = new GlobalHotkeys(new WindowInteropHelper(this).Handle, OnHotkey);
            ApplyHotkeys();
        };
        // Bilgisayarda gösterilecek çeviriler küçük, üstte duran bir kutuda çıkar.
        controller.PcResultReady += PcResultWindow.Display;
        // Bilgisayar ekranındaki altyazı penceresi (altyazı yokken gizli). Kurulamazsa uygulama onsuz açılır.
        SubtitleOverlayWindow? overlay = null;
        try
        {
            overlay = SubtitleOverlayWindow.Attach(controller);
        }
        catch (Exception error)
        {
            StartupGuard.Log("Altyazı penceresi kurulamadı", error);
            StartupGuard.Show(Application.Current, StartupGuard.OverlayFailedMessage(error));
        }
        // Sistem tepsisi simgesi ve açılır penceresi (Mac'teki menü çubuğunun karşılığı).
        var tray = TrayPopup.Attach(controller);
        Closed += (_, _) =>
        {
            tray?.Detach();
            ThemeManager.Changed -= PaintCaption;
            if (current == this) current = null;
            controller.PcResultReady -= PcResultWindow.Display;
            overlay?.Detach();
            hotkeys?.Dispose();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            controller.TvPairingRequested -= OnTvPairing;
            controller.Dispose();
        };
        ShowScreen(force: true);
        ShowUpdate();
        ShowAnnouncement();
    }

    private bool pairingShown;

    /// <summary>
    /// "Bu TV eşleşsin mi?" penceresi. Pencere açıkken gelen yeni istek (TV'de Tamam ile yeniden denendi, TV uygulaması
    /// yeniden açıldı) ikinci pencere açmaz; pencere kapanınca bekleyen istek yeni koduyla gösterilir (Mac ile aynı).
    /// </summary>
    private void OnTvPairing(SubtitlePairing.Ticket ticket)
    {
        // Kurulumun TV adımında kod kartta onaylanır; açılır kart ayrıca sorulmaz.
        if (pairingShown || Views.Steps.TvCodeCard.Showing) return;
        pairingShown = true;
        try
        {
            var text = Localizer.Current;
            for (var current = ticket; ;)
            {
                var message = text.Format("TV code: %@. You don't type this code anywhere: if your TV shows the same code, approve it here.", current.Code);
                var approved = DesignDialog.Confirm(this, text["Pair this TV?"], message, text["I See This Code on the TV"], text["Cancel"]);
                var resolved = controller.ResolveTvPairing(current.Id, approved);
                if (controller.PendingTvPairing is { } next && next.Id != current.Id) { current = next; continue; }
                if (!resolved && approved)
                    MessageBox.Show(this, text["The pairing request expired. Try again on the TV."], "Çevirgen");
                break;
            }
        }
        finally { pairingShown = false; }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppController.Step) or nameof(AppController.DemoMode))
        {
            // Ana ekrandan kuruluma dönülünce (TV Bağla, Yeniden Kur) erteleme biter, sihirbaz açılır.
            if (e.PropertyName is nameof(AppController.Step) && setupDeferred && controller.Step != OnboardingStep.Done) setupDeferred = false;
            ShowScreen();
        }
        if (e.PropertyName is nameof(AppController.IsRunning) or nameof(AppController.IsStarting) or nameof(AppController.DemoMode)
            or nameof(AppController.Step) or nameof(AppController.Ps5Tone) or nameof(AppController.TvTone) or nameof(AppController.SessionState)
            or nameof(AppController.Settings) or nameof(AppController.CanStart) or nameof(AppController.TvClients) or nameof(AppController.Tv)
            or nameof(AppController.Console) or nameof(AppController.TranslatorStatus) or nameof(AppController.OnlineIssue))
            ShowStatus();
        if (e.PropertyName is nameof(AppController.Settings)) Motion.AnimationsEnabled = controller.Settings.TransitionAnimations;
        if (e.PropertyName is nameof(AppController.IsRunning)) MotionGate.Translating = controller.IsRunning;
        // Çeviri durunca son gerçek kare saklanır: Canlı ekran ve Alanları düzenle sonraki açılışta sahte sahne yerine onu gösterir.
        if (e.PropertyName is nameof(AppController.IsRunning) && !controller.IsRunning && !controller.DemoMode) LastFrameStore.Save(controller.LastFrame);
        if (e.PropertyName is nameof(AppController.IsRunning) && !controller.IsRunning && showingMain)
            PopupAdWindow.Offer(this, controller, PopupAdMoment.TranslationStopped);
        // Açılır reklam açılıştan sonra sunucudan geldiyse (ilk açılış, önbellek yok) açılış anı sayılır; kurallar aynı.
        if (e.PropertyName is nameof(AppController.RemotePopups) && showingMain && controller.RemotePopups.Count > 0)
            Dispatcher.BeginInvoke(() => PopupAdWindow.Offer(this, controller, PopupAdMoment.AppLaunch), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (e.PropertyName is nameof(AppController.IsRunning) or nameof(AppController.HotkeysPaused) or nameof(AppController.Settings))
            ApplyHotkeys();
        if (e.PropertyName is nameof(AppController.UpdateVersion) or nameof(AppController.Updating)
            or nameof(AppController.UpdateProgress) or nameof(AppController.UpdateError))
            ShowUpdate();
        if (e.PropertyName is nameof(AppController.CurrentAnnouncement)) ShowAnnouncement();
    }

    /// <summary>Kısayol tablosu ayarlardan gelir; Ayarlar'da yeni tuş beklenirken kısayollar bırakılır.</summary>
    private void ApplyHotkeys()
    {
        if (hotkeys is null) return;
        hotkeys.Bindings = HotkeyTable.Resolve(controller.Settings.Hotkeys);
        hotkeys.Enabled = controller.IsRunning && !controller.HotkeysPaused;
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ExtraRegion1: _ = controller.TranslateExtraRegionAsync(1); break;
            case HotkeyAction.ExtraRegion2: _ = controller.TranslateExtraRegionAsync(2); break;
            case HotkeyAction.ExtraRegion3: _ = controller.TranslateExtraRegionAsync(3); break;
            case HotkeyAction.PauseSubtitles: controller.ToggleSubtitles(); break;
            case HotkeyAction.Snapshot: controller.RequestSnapshot(); break;
            case HotkeyAction.FreeArea: FreeAreaWindow.Open(controller); break;
            case HotkeyAction.Screen: _ = controller.TranslateScreenAsync(); break;
            case HotkeyAction.ClickThrough: controller.ToggleClickThrough(); break;
            case HotkeyAction.HideSubtitles: controller.ToggleSubtitlesHidden(); break;
        }
    }

    private void ShowScreen(bool force = false)
    {
        // Deneme modunda kurulum bitmemiş olsa da ana ekran gösterilir; çıkınca kalınan adıma dönülür.
        var main = controller.Step == OnboardingStep.Done || controller.DemoMode || setupDeferred;
        ShowStatus();
        if (!force && main == showingMain) return;
        showingMain = main;
        // Kurulumda üst çubuk, duyuru ve reklam bandı yok (sihirbazın kendi çubuğu var). Güncelleme şeridi her yerde.
        TopBar.Visibility = main ? Visibility.Visible : Visibility.Collapsed;
        ApplyAds();
        ShowAnnouncement();
        home?.Detach();
        if (!main)
        {
            home = null;
            Show(new OnboardingView(controller));
            return;
        }
        home = new MainView(controller);
        _ = controller.CheckForUpdateAsync();
        // Açılır reklam: yalnız açılışta ya da çeviri durunca, günde en fazla bir kez (PopupAdPolicy).
        Dispatcher.BeginInvoke(() => PopupAdWindow.Offer(this, controller, PopupAdMoment.AppLaunch), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        tab = ShellTab.Home;
        Show(home);
        SelectTab(ShellTab.Home, null);
    }

    /// <summary>Kurulum "Sonra Ayarlarım" ile bırakıldı (bu oturumda): ana ekran "Kurulum Eksik" durumuyla gösterilir.</summary>
    private bool setupDeferred;

    /// <summary>Kurulum sihirbazındaki "Sonra Ayarlarım".</summary>
    public static void DeferSetup()
    {
        if (current is not { } window) return;
        window.setupDeferred = true;
        window.ShowScreen();
    }

    /// <summary>Yalnız ekran görüntüsü aracı (--render-screens): pencere gösterilmeden sekme ve Ayarlar bölümü seçilir.</summary>
    internal void ShowForRender(ShellTab chosen, SettingsGroup? group = null)
    {
        if (!showingMain) return;
        SelectTab(chosen, null);
        if (group is { } page && Host.Content is SettingsView settings) settings.Show(page);
    }

    /// <summary>Ekran görüntüsü aracı: canlı ekranın kendisi (örnek altyazı olayları vermek için).</summary>
    internal MainView? Home => home;

    /// <summary>Başka bir yerden sekme açar (Canlı ekrandaki bağlantılar, menüler, kısayollar).</summary>
    public static void Navigate(ShellTab tab, SettingsSection? section = null)
    {
        if (current is not { } window) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (!window.showingMain) return;
        // Eski bölüm adları: Görünüm ve çeviri motoru artık kendi sekmelerinde.
        if (tab == ShellTab.Settings && section == SettingsSection.Appearance) { window.SelectTab(ShellTab.Appearance, null); return; }
        if (tab == ShellTab.Settings && section == SettingsSection.Translation) { window.SelectTab(ShellTab.Translation, null); return; }
        window.SelectTab(tab, section is { } wanted ? SettingsView.GroupFor(wanted) : null);
    }

    /// <summary>
    /// "Alanları düzenle" (Canlı ekrandaki bağlantı, Ayarlar › Okuma alanları): düzenleyici pencerenin tamamını kaplar (üst
    /// çubuk, duyuru ve reklam bandı gizlenir); "Bitti" ya da "Vazgeç" ile önceki ekrana dönülür.
    /// </summary>
    public static void EditAreas()
    {
        if (current is not { } window || !window.showingMain || window.Host.Content is AreaEditorView) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        var previous = window.Host.Content as FrameworkElement;
        window.TopBar.Visibility = Visibility.Collapsed;
        window.AnnouncementBar.Visibility = Visibility.Collapsed;
        window.Ads.Visibility = Visibility.Collapsed;
        window.Show(new AreaEditorView(window.controller, () =>
        {
            window.TopBar.Visibility = window.showingMain ? Visibility.Visible : Visibility.Collapsed;
            window.ApplyAds();
            window.ShowAnnouncement();
            // Ayarlar sayfası yeni alanlarla yeniden kurulur; Canlı ekran saklıdır, aynen döner.
            if (previous is SettingsView) window.SelectTabFresh(ShellTab.Settings, SettingsGroup.Areas);
            else if (previous is MainView) window.SelectTabFresh(ShellTab.Home, null);
            else if (previous is not null) window.Show(previous);
        }));
    }

    /// <summary>Sekmeyi (açıksa da) yeniden kurarak açar; Canlı ekran saklı olanıdır.</summary>
    private void SelectTabFresh(ShellTab chosen, SettingsGroup? group)
    {
        tab = (ShellTab)(-1);
        SelectTab(chosen, group);
    }

    /// <summary>Ayarlar'ı belli bir bölümde açar.</summary>
    public static void Navigate(SettingsGroup group)
    {
        if (current is not { } window) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (window.showingMain) window.SelectTab(ShellTab.Settings, group);
    }

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (switching || !showingMain) return;
        var chosen = sender == AppearanceTab ? ShellTab.Appearance : sender == TranslationTab ? ShellTab.Translation
            : sender == HistoryTab ? ShellTab.History : sender == SettingsTab ? ShellTab.Settings : ShellTab.Home;
        SelectTab(chosen, null);
    }

    private void OnWordmark(object sender, RoutedEventArgs e)
    {
        if (showingMain) SelectTab(ShellTab.Home, null);
    }

    /// <summary>
    /// Sekmeyi açar. Canlı ekran bir kez kurulur ve saklanır (canlı altyazı, oyun durumu kaybolmasın); diğer sayfalar her
    /// açılışta yeniden kurulur (güncel ayarlarla).
    /// </summary>
    private void SelectTab(ShellTab chosen, SettingsGroup? group)
    {
        switching = true;
        (chosen switch
        {
            ShellTab.Appearance => AppearanceTab,
            ShellTab.Translation => TranslationTab,
            ShellTab.History => HistoryTab,
            ShellTab.Settings => SettingsTab,
            _ => HomeTab,
        }).IsChecked = true;
        switching = false;
        // Ayarlar'da sağda "Kurulumu baştan başlat"; diğer sekmelerde cihaz durumu ve başlat/durdur.
        RestartSetup.Visibility = chosen == ShellTab.Settings ? Visibility.Visible : Visibility.Collapsed;
        StatusArea.Visibility = chosen == ShellTab.Settings ? Visibility.Collapsed : Visibility.Visible;
        if (chosen == tab && Host.Content is FrameworkElement shown && shown is not OnboardingView)
        {
            if (group is { } wanted && shown is SettingsView open) open.Show(wanted);
            return;
        }
        tab = chosen;
        if (chosen == ShellTab.Home)
        {
            Show(home ??= new MainView(controller));
            return;
        }
        var page = Pages[chosen](controller);
        if (group is { } section && page is SettingsView settings) settings.Show(section);
        Show(page);
    }

    /// <summary>Reklam bandı yalnız ana ekranlarda ve reklam varken.</summary>
    private void ApplyAds() =>
        Ads.Visibility = showingMain && AdCatalog.Banners.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Sayfa geçişi: yeni sayfa kısaca solarak gelir (hareket azaltılmışsa yalnız solma).</summary>
    private void Show(FrameworkElement page)
    {
        Host.Content = page;
        Motion.EnterFrom(page, 4, Motion.Page);
    }

    // ───── Cihaz durumu ve başlat/durdur ─────

    /// <summary>
    /// Üst çubuğun sağı: "● PS5-010  ● Oturma Odası TV'si  ● Gemini 2.5 Flash" (yeşil hazır, sarı-turuncu bekleniyor,
    /// kırmızı hata, gri kapalı; <see cref="DeviceStates"/>) ve çeviriyi başlat/durdur düğmesi.
    /// </summary>
    private void ShowStatus()
    {
        StatusLine.Children.Clear();
        foreach (var item in DeviceStatus.Items(controller))
        {
            var row = DeviceStatus.Line(item.State, item.Text);
            row.Margin = Sp.T("0", "0", "m", "0");
            row.ToolTip = item.Hint;
            System.Windows.Automation.AutomationProperties.SetName(row, $"{item.Text}: {item.State.Name()}. {item.Hint}");
            StatusLine.Children.Add(row);
        }
        ShowToggle();
    }

    /// <summary>Başlat/durdur: çeviri sürerken kareli "Çeviriyi durdur", durmuşken "Çeviriyi başlat"; deneme modunda çıkış.</summary>
    private void ShowToggle()
    {
        var text = Localizer.Current;
        var running = controller.IsRunning;
        var demo = controller.DemoMode;
        Toggle.Style = (Style)FindResource(running || demo ? "LiveButton" : "SecondaryButton");
        Toggle.Content = controller.IsStarting ? text["Starting…"] : demo ? text["Exit Demo Mode"] : running ? "Çeviriyi durdur" : "Çeviriyi başlat";
        // Altyazının gideceği bir yer yoksa (TV seçili ama bağlı değil) çeviri başlatılamaz.
        Toggle.IsEnabled = !controller.IsStarting && (running || demo || controller.CanStart);
    }

    private async void OnToggle(object sender, RoutedEventArgs e) => await ToggleTranslationAsync(controller);

    /// <summary>
    /// Çeviriyi başlatır ya da durdurur (deneme modunda çıkar). async void'den kaçan hata genel hata işleyicisine düşerdi;
    /// hata kaydedilir ve gösterilir, uygulama açık kalır. Canlı ekrandaki büyük "Çeviriyi başlat" da bunu çağırır.
    /// </summary>
    internal static async Task ToggleTranslationAsync(AppController controller)
    {
        var starting = !controller.IsRunning;
        try
        {
            if (controller.DemoMode) controller.ExitDemo();
            else if (starting) await controller.StartAsync(); else controller.Stop();
        }
        catch (Exception error)
        {
            StartupGuard.Log(starting ? "Çeviri başlatılamadı" : "Çeviri durdurulamadı", error);
            StartupGuard.Show(Application.Current, StartupGuard.ErrorMessage(error));
        }
    }

    /// <summary>"Kurulumu baştan başlat": onaydan sonra kayıtlı her şey (hesap, eşleşmeler) silinir, sihirbaz baştan açılır.</summary>
    private void OnRestartSetup(object sender, RoutedEventArgs e) => SettingsView.ConfirmReset(this, controller);

    // ───── Güncelleme şeridi ─────

    /// <summary>Yeni sürüm varken en üstte tam genişlikte sarı şerit: başlık, açıklama/hata, ilerleme, "Şimdi güncelle".</summary>
    private void ShowUpdate()
    {
        if (notesVersion is not null && notesVersion != controller.UpdateVersion)
        {
            UpdateNotes.Visibility = Visibility.Collapsed;
            UpdateNotesText.Inlines.Clear();
            notesVersion = null;
        }
        UpdateBanner.Visibility = controller.UpdateVersion is null ? Visibility.Collapsed : Visibility.Visible;
        if (controller.UpdateVersion is null) return;
        UpdateTitle.Text = Localizer.Brand(controller.UpdateTitle);
        UpdateButton.Content = Localizer.Brand(controller.UpdateButtonText);
        UpdateButton.IsEnabled = !controller.Updating;
        UpdateBar.Visibility = controller.Updating ? Visibility.Visible : Visibility.Collapsed;
        UpdateBar.Value = controller.UpdateProgress;
        var problem = controller.UpdateError ?? controller.UpdateHint;
        UpdateHintText.Text = problem is null ? "" : Localizer.Brand(problem);
        UpdateHintText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnUpdate(object sender, RoutedEventArgs e) => await controller.InstallUpdateAsync();

    /// <summary>"Sürüm notları": şeridin altında açılır/kapanır; notlar GitHub sürüm sayfasından bir kez yüklenir.</summary>
    private async void OnUpdateNotes(object sender, RoutedEventArgs e)
    {
        if (UpdateNotes.Visibility == Visibility.Visible)
        {
            UpdateNotes.Visibility = Visibility.Collapsed;
            return;
        }
        UpdateNotes.Visibility = Visibility.Visible;
        if (controller.UpdateVersion is not { } version || notesVersion == version) return;
        notesVersion = version;
        UpdateNotesText.Text = Localizer.Current["Loading release notes…"];
        var notes = await controller.LoadUpdateNotesAsync(version);
        if (controller.UpdateVersion != version || notesVersion != version) return;
        UpdateNotesText.Inlines.Clear();
        foreach (var note in notes)
        {
            var run = new Run(note.Text);
            if (note.Bold) run.FontWeight = FontWeights.SemiBold;
            if (note.Link is not { } url) UpdateNotesText.Inlines.Add(run);
            else
            {
                var link = new Hyperlink(run) { NavigateUri = url, Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x10, 0x14)) };
                link.RequestNavigate += (_, args) => { controller.OpenInBrowser(args.Uri); args.Handled = true; };
                UpdateNotesText.Inlines.Add(link);
            }
        }
        if (notes.Count == 0)
        {
            UpdateNotesText.Text = Localizer.Current["Release notes are unavailable."];
            notesVersion = null;
        }
    }

    // ───── Duyuru ─────

    /// <summary>Sunucudaki duyuru (kullanıcı kapatmadıysa): yalnız ana ekranlarda; bağlantısı varsa "Aç →".</summary>
    private void ShowAnnouncement()
    {
        var announcement = controller.CurrentAnnouncement;
        AnnouncementBar.Visibility = showingMain && announcement is not null ? Visibility.Visible : Visibility.Collapsed;
        AnnouncementText.Text = announcement?.Text ?? "";
        AnnouncementOpen.Visibility = announcement?.Link is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenAnnouncement(object sender, RoutedEventArgs e) => controller.OpenAnnouncement();

    private void OnDismissAnnouncement(object sender, RoutedEventArgs e)
    {
        if (controller.CurrentAnnouncement is { } announcement) controller.DismissAnnouncement(announcement.Id);
    }

    /// <summary>
    /// En küçük boy 1200 × 760 (tasarım). Ekranın çalışma alanı bundan küçükse (1366 × 768 dizüstü) pencere ekrana
    /// sığacak kadar küçülebilir; yoksa pencerenin altı görev çubuğunun arkasında kalırdı.
    /// </summary>
    private void ApplyMinimumSize()
    {
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(Palette.Size["windowWidth"], work.Width);
        MinHeight = Math.Min(Palette.Size["windowHeight"], work.Height);
        Width = Math.Max(MinWidth, Math.Min(Palette.Size["windowWidth"], work.Width));
        Height = Math.Max(MinHeight, Math.Min(Palette.Size["windowHeight"], work.Height));
    }

    /// <summary>
    /// Windows 11'de başlık çubuğu pencere zemininin rengini alır (koyu temada koyu başlık); üst çubukla bir bütün görünür.
    /// Eski Windows'ta bu öznitelik yoktur, sistem başlığı kalır.
    /// </summary>
    private void PaintCaption()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        try
        {
            var dark = ThemeManager.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            if (TryFindResource("backgroundColor") is Color background)
            {
                var colorRef = background.R | background.G << 8 | background.B << 16;
                DwmSetWindowAttribute(handle, DwmCaptionColor, ref colorRef, sizeof(int));
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.Invoke(controller.SystemResumed);
    }
}
