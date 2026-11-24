// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CouchSubs.App.Theme;
using CouchSubs.App.Views;
using CouchSubs.Core;
using CouchSubs.Core.LgTv;
using CouchSubs.RemotePlay;
using CouchSubs.Shell;

namespace CouchSubs.App.Services;

/// <summary>
/// Geliştirme aracı: <c>CouchSubs.exe --render-screens klasör</c> bütün ekranları açık ve koyu temada PNG'ye
/// döker ve kapanır. Ekranların görünümü Windows makinesi olmadan (sürekli tümleştirmede) böyle denetlenir.
/// </summary>
public static class ScreenRenderer
{
    /// <summary>Uygulama penceresinin en küçük boyu (1200 × 760; Theme/Palette.cs).</summary>
    private static int Width => (int)Palette.Size["windowWidth"];
    private static int Height => (int)Palette.Size["windowHeight"];

    public static void Run(Application app, string folder)
    {
        Directory.CreateDirectory(folder);
        // Açılan ilk pencere kendiliğinden ana pencere sayılır; kapanınca uygulama da kapanmasın.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Görüntüler durağan karelerle alınır (ortam ışığı, nabız, arama halkası; hareket kapısı kapalı).
        MotionGate.Forced = false;
        var log = Path.Combine(folder, "errors.txt");
        // Bir ekranın hatası diğerlerini durdurmasın; hatalar dosyaya yazılır.
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.AppendAllText(log, $"[dispatcher] {e.Exception}\n\n");
            e.Handled = true;
        };
        foreach (var dark in new[] { false, true })
        {
            ThemeManager.Apply(app.Resources, dark);
            var suffix = dark ? "koyu" : "acik";
            // Yeni arayüzün ortak bileşenleri (tasarım sistemi).
            Save(() => new DesignGallery(), Width, Height, Path.Combine(folder, $"bilesenler-{suffix}.png"));
            foreach (var step in Enum.GetValues<OnboardingStep>().Where(s => s != OnboardingStep.Done))
            {
                var controller = Demo(step);
                Save(() => new OnboardingView(controller), Width, Height, Path.Combine(folder, $"adim-{(int)step + 1}-{step}-{suffix}.png"));
            }
            // TV kurulum ekranının durumları (Mac kurulum-tv-1…6 ile aynı sahte liste): boş, taranıyor, bulundu, onay bekleniyor, hata.
            LgTv lg = new("LG OLED55C3", "192.168.1.153"), samsung = new("Samsung QE55Q80", "192.168.1.160", TvBrand.Samsung);
            var tvStates = new (string Name, Views.Steps.TvStep.Preview State)[]
            {
                ("1-bos", new([])),
                ("2-tariyor", new([], Searching: true)),
                ("3-bulundu", new([lg, samsung])),
                ("4-onay", new([lg, samsung], PairingHost: lg.Host)),
                ("6-hata", new([lg, samsung], PairError: TvAppMessages.PairingProblem(new TimeoutException(), TvBrand.Samsung, Localizer.Current))),
            };
            foreach (var (name, state) in tvStates)
            {
                Views.Steps.TvStep.PreviewState = state;
                Save(() => new OnboardingView(Demo(OnboardingStep.Tv)), Width, Height, Path.Combine(folder, $"kurulum-tv-{name}-{suffix}.png"));
            }
            // Eşleşmiş TV'li durumlar (Mac kurulum-tv-5 / tv-7): TV uygulaması kuruluyor; Samsung'da geliştirici modu kapalı
            // (ön koşul listesi). TV adımı eşleşmiş TV'yi kayıttan okur; uygulama durumu yalnız bu araçta doğrudan verilir.
            foreach (var (name, paired, tvApp) in new[]
                     {
                         ("5-kuruluyor", lg, new TvAppState(TvAppKind.Working)),
                         ("7-samsung-onkosul", samsung, new TvAppState(TvAppKind.DevModeOff)),
                     })
            {
                Views.Steps.TvStep.PreviewState = new([paired]);
                Save(() =>
                {
                    var controller = Demo(OnboardingStep.Tv, paired);
                    typeof(AppController).GetProperty(nameof(AppController.TvApp))!.GetSetMethod(nonPublic: true)!.Invoke(controller, [tvApp]);
                    return new OnboardingView(controller);
                }, Width, Height, Path.Combine(folder, $"kurulum-tv-{name}-{suffix}.png"));
            }
            Views.Steps.TvStep.PreviewState = null;
            Save(() => new MainView(Demo(OnboardingStep.Done)), Width, Height, Path.Combine(folder, $"ana-{suffix}.png"));
            Save(() => new HistoryView(Demo(OnboardingStep.Done)), Width, Height, Path.Combine(folder, $"gecmis-{suffix}.png"));
            // Uygulamanın gerçek penceresi: XAML'ı (simge dahil) açılıştaki gibi yüklenir.
            Save(() => Detach(new ShellWindow(Demo(OnboardingStep.Done))), Width, Height, Path.Combine(folder, $"pencere-{suffix}.png"));
            // Çevirgen arayüzü (1. bölüm): kabuk, Canlı (çeviri sürerken ve boş hâl), güncelleme şeridi ve duyuru, platform
            // sorusu ve pencere adımı, Ayarlar'ın altı bölümü, geçici Görünüm ve Çeviri sekmeleri.
            Save(() => Detach(Live(Running())), Width, Height, Path.Combine(folder, $"pencere-ana-{suffix}.png"));
            Save(() => Detach(Live(Running())), 1680, 945, Path.Combine(folder, $"pencere-genis-ana-{suffix}.png"));
            Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Home)), Width, Height, Path.Combine(folder, $"pencere-canli-bos-{suffix}.png"));
            Save(() =>
            {
                var controller = Running();
                SetPrivate(controller, nameof(AppController.UpdateVersion), "1.2.0");
                typeof(AppController).GetField("currentAnnouncement", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(controller, new Announcement("demo", "Çevirgen TV 1.0.2 yayında: Samsung'da altyazı artık daha hızlı açılıyor.", new Uri("https://example.com")));
                return Detach(Live(controller));
            }, Width, Height, Path.Combine(folder, $"pencere-guncelleme-{suffix}.png"));
            Save(() => Detach(Shell(Running(), ShellTab.History)), Width, Height, Path.Combine(folder, $"pencere-gecmis-{suffix}.png"));
            Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Appearance)), Width, Height, Path.Combine(folder, $"pencere-gorunum-{suffix}.png"));
            Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Translation)), Width, Height, Path.Combine(folder, $"pencere-ceviri-{suffix}.png"));
            Save(() =>
            {
                var controller = Running();
                var window = Live(controller);
                controller.AskGame();
                return Detach(window);
            }, Width, Height, Path.Combine(folder, $"pencere-oyun-sor-{suffix}.png"));
            foreach (var platform in new[] { SetupPlatform.Ps5, SetupPlatform.Pc })
                Save(() =>
                {
                    var controller = Demo(OnboardingStep.Welcome);
                    controller.ChoosePlatform(platform);
                    return new OnboardingView(controller);
                }, Width, Height, Path.Combine(folder, $"kurulum-platform-{platform.ToString().ToLowerInvariant()}-{suffix}.png"));
            Save(() =>
            {
                var controller = Demo(OnboardingStep.Welcome);
                controller.ChoosePlatform(SetupPlatform.Capture);
                controller.Advance();
                return new OnboardingView(controller);
            }, Width, Height, Path.Combine(folder, $"kurulum-pencere-{suffix}.png"));
            foreach (var (group, name) in SettingsPages)
                Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Settings, group)), Width, Height, Path.Combine(folder, $"pencere-ayarlar-{name}-{suffix}.png"));
            Save(() => new SettingsView(Demo(OnboardingStep.Done)), Width, Height, Path.Combine(folder, $"ayarlar-{suffix}.png"));
            foreach (var (group, name) in SettingsPages.Skip(1))
                Save(() =>
                {
                    var page = new SettingsView(Demo(OnboardingStep.Done));
                    page.Show(group);
                    return page;
                }, Width, Height, Path.Combine(folder, $"ayarlar-{name}-{suffix}.png"));
            Save(() => Detach(new AboutWindow()), 460, 640, Path.Combine(folder, $"hakkinda-{suffix}.png"));
        }
        app.Shutdown();
    }

    /// <summary>Ayarlar'ın bölümleri ve dosya adları.</summary>
    private static readonly (SettingsGroup Group, string Name)[] SettingsPages =
    [
        (SettingsGroup.Devices, "cihazlar"), (SettingsGroup.Areas, "alanlar"), (SettingsGroup.Dubbing, "seslendirme"),
        (SettingsGroup.Shortcuts, "kisayollar"), (SettingsGroup.Advanced, "gelismis"), (SettingsGroup.App, "uygulama"),
    ];

    /// <summary>Bir ekran görüntüsünde çizilen PNG sayısı (iki tema): sürekli tümleştirme en az bu kadarını bekler.</summary>
    public const int ExpectedCount = 90;

    /// <summary>Uygulama penceresi, istenen sekme (ve Ayarlar bölümü) açık.</summary>
    private static ShellWindow Shell(AppController controller, ShellTab tab, SettingsGroup? group = null)
    {
        var window = new ShellWindow(controller);
        window.ShowForRender(tab, group);
        return window;
    }

    /// <summary>
    /// Canlı ekran çeviri sürerken: son kare (İngilizce iki satırla) ve TV'ye gidiyormuş gibi iki Türkçe satır (üstteki eski
    /// satır "above"); dökümde ikisi "● ekranda".
    /// </summary>
    private static ShellWindow Live(AppController controller)
    {
        var window = Shell(controller, ShellTab.Home);
        window.Home?.ShowEventForRender(new SubtitleEvent(SubtitleEventKind.Subtitle, "Hâlâ burada mı?", 60000,
            Box: new CouchSubs.Core.TextBox(0.3, 0.78, 0.7, 0.9), Above: ["Depolama alanı hiç fazla gelmez, değil mi?"]));
        return window;
    }

    /// <summary>
    /// Çeviri sürüyormuş gibi örnek oturum (taslak Main.dc.html'deki satırlar): son kare, oturumun başı, kaynakları ve okuma
    /// hızlarıyla döküm, oynanan oyun.
    /// </summary>
    private static AppController Running()
    {
        var controller = Demo(OnboardingStep.Done);
        var history = (SubtitleHistory)typeof(AppController).GetField("history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        var start = DateTime.UtcNow.AddMinutes(-42).AddSeconds(-20);
        typeof(AppController).GetField("sessionStartedAt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, start);
        foreach (var (seconds, source, translation, origin, cps) in new (double, string, string, LineOrigin, double?)[]
                 {
                     (2518.903, "Welcome back to Vice City, bro.", "Vice City'ye tekrar hoş geldin, kanka.", LineOrigin.Engine, 12.4),
                     (2522.540, "Y'all selling that trailer right there?", "Şuradaki karavanı satıyor musunuz?", LineOrigin.Engine, 11.8),
                     (2526.218, "I'm thinkin' of doing a little cross-country vacay with a lady friend of mine.",
                         "Bir hanım arkadaşımla ülke boyunca ufak bir tatile çıkmayı düşünüyorum.", LineOrigin.Engine, 21.2),
                     (2529.402, "Whip the ever-lovin' shit outta him!", "Ağzını burnunu kır şunun!", LineOrigin.Correction, 9.6),
                     (2533.154, "Can never have too much storage space, am I right?", "Depolama alanı hiç fazla gelmez, değil mi?", LineOrigin.Pack, 13.1),
                     (2535.880, "He still here?", "Hâlâ burada mı?", LineOrigin.Engine, null),
                 })
        {
            var at = start.AddSeconds(seconds);
            history.Add(source, translation, origin, at);
            if (cps is { } speed)
            {
                // Okuma hızı satır ekrandan kalkınca yazılır (MarkLeft); örnekte doğrudan.
                var entries = (List<SubtitleHistory.Entry>)typeof(SubtitleHistory).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(history)!;
                var index = entries.FindIndex(entry => entry.Source == source);
                if (index >= 0) entries[index] = entries[index] with { ShownAt = at, Cps = speed };
            }
        }
        typeof(AppController).GetProperty(nameof(AppController.IsRunning))!.GetSetMethod(nonPublic: true)!.Invoke(controller, [true]);
        SetPrivate(controller, nameof(AppController.LastTranslation), "Hâlâ burada mı?");
        SetPrivate(controller, nameof(AppController.PlayedGame), "Grand Theft Auto VI");
        SetPrivate(controller, nameof(AppController.LastFrame),
            DemoFrameRenderer.Draw(["Can never have too much storage space, am I right?", "He still here?"], 2));
        return controller;
    }

    private static void SetPrivate(AppController controller, string property, object? value) =>
        typeof(AppController).GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(controller, [value]);

    /// <summary>Pencerenin içeriğini başka bir pencerede çizebilmek için kendi penceresinden ayırır.</summary>
    private static FrameworkElement Detach(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        return content;
    }

    internal static AppController Demo(OnboardingStep step, LgTv? pairedTv = null)
    {
        var controller = new AppController(new DemoServices(), new DemoStore(step == OnboardingStep.Done, pairedTv),new DemoSettings(), new DemoSleep(),
            Localizer.Current, action => action());
        if (step != OnboardingStep.Done) controller.Step = step;
        return controller;
    }

    private static void Save(Func<FrameworkElement> make, int width, int height, string path)
    {
        try
        {
            Render(make(), width, height, path);
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "errors.txt"), $"[{Path.GetFileName(path)}] {error}\n\n");
        }
    }

    private static void Render(FrameworkElement view, int width, int height, string path)
    {
        // Görüntüye pencere zemini de girsin (içerik saydam zemin üzerinde çizilir).
        var root = new System.Windows.Controls.Border { Child = view, Width = width, Height = height };
        root.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "windowBackgroundBrush");
        root.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "textPrimaryBrush");
        var window = new Window
        {
            Content = new System.Windows.Controls.Canvas { Children = { root } }, Width = width, Height = height, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            Left = -10000, Top = -10000, ShowActivated = false,
        };
        window.SetResourceReference(Window.BackgroundProperty, "backgroundBrush");
        // WPF pencereyi ekrandan büyük açmaz (CI'daki sanal ekran 1024 × 768). Görüntü istenen boyda (1200 × 760, geniş
        // 1680 × 945) çizilsin diye içerik, sınır koymayan bir Canvas içinde kendi boyunda yerleşir ve doğrudan çizilir.
        window.Show();
        window.UpdateLayout();
        // Yükleme sırasında başlayan arama vb. işlerin ekrana yansıması için kısa bekleme.
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);

        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        window.Close();
    }

    private sealed class DemoServices : IAppServices
    {
        public ITranslationModel Translation { get; } = new DemoTranslation();
        public Task<IReadOnlyList<ConsoleInfo>> DiscoverConsolesAsync() => Task.FromResult<IReadOnlyList<ConsoleInfo>>([new("192.168.1.20", "PS5-123", true)]);
        public Task<byte[]> AccountIdFromRedirectAsync(Uri redirect) => Task.FromResult(new byte[8]);
        public Task<RegisteredConsole> RegisterAsync(string host, byte[] accountId, uint pin) => throw new NotSupportedException();
        public Task<IReadOnlyList<LgTv>> DiscoverTvsAsync() => Task.FromResult<IReadOnlyList<LgTv>>([new("[LG] webOS TV", "192.168.1.30")]);
        public Task<TvPairing> PairTvAsync(LgTv tv, string? key, string server, string accessKey) => throw new NotSupportedException();
        public IStreamSession MakeStream(RegisteredConsole console, byte[] accountId, string? loginPasscode) => throw new NotSupportedException();
        public IKeeper MakeKeeper(LgTv tv, string? key, IReadOnlyDictionary<string, string> parameters, Func<bool> appConnected, Action<LgTv> onMoved,
                                  Action<string> onKey, Func<int?>? consolePort = null,
                       Action<TvOpenProblem?>? onOpenResult = null) => throw new NotSupportedException();
        public string? BundledTvAppVersion => null;
        public Task<bool> IsDevModeOnAsync(string host) => Task.FromResult(false);
        public Task<string> FetchDevKeyAsync(string host, string passphrase) => throw new NotSupportedException();
        public Task<string?> InstalledTvAppVersionAsync(string host, string devKey) => throw new NotSupportedException();
        public Task InstallTvAppAsync(string host, string devKey) => throw new NotSupportedException();
        public Task<int?> RenewDevSessionAsync(string host, string devKey) => Task.FromResult<int?>(null);
        public BundledTvApp? BundledApp(TvBrand brand) => null;
        public Task<bool> IsSamsungDevModeOnAsync(string host) => Task.FromResult(false);
        public Task<(string Version, bool Installed)> InstallSamsungAppAsync(string host, BundledTvApp app, bool openIfInstalled, Action<TvInstallEvent> onEvent) => throw new NotSupportedException();
        public Task<bool> IsAndroidDebugOnAsync(string host) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> ScanAndroidTvsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> InstallAndroidAppAsync(string host, BundledTvApp app, bool openIfInstalled, Action<TvInstallEvent> onEvent) => throw new NotSupportedException();
        public ITextReader? MakeReader(string language) => null;
        // Kurulumun pencere adımı için örnek pencereler.
        public IReadOnlyList<WindowCaptureTarget> CaptureWindows() =>
            [new("obs64", "Projektör (Kaynak) - Elgato HD60 X"), new("GTA5", "Grand Theft Auto V"), new("4KCaptureUtility", "4K Capture Utility")];
    }

    private sealed class DemoTranslation : ITranslationModel
    {
        public bool IsSupported(string source, string target) => source == "en";
        public bool IsInstalled(string source, string target) => true;
        public Task DownloadAsync(string source, string target, IProgress<double> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public ITextTranslator Create(string source, string target) => throw new NotSupportedException();
    }

    private sealed class DemoStore(bool ready, LgTv? pairedTv = null) : IAppStore
    {
        public void Save<T>(string key, T value) { }
        public T? Load<T>(string key) => pairedTv is not null && key is StoreKeys.Tv or StoreKeys.TvKey
            ? (T)(key == StoreKeys.Tv ? pairedTv : (object)"key")
            : !ready ? default : key switch
        {
            StoreKeys.AccountId => (T)(object)new byte[8],
            StoreKeys.Console => (T)(object)new RegisteredConsole("192.168.1.20", "PS5-123", new byte[16], new byte[16]),
            StoreKeys.Tv => (T)(object)new LgTv("[LG] webOS TV", "192.168.1.30"),
            StoreKeys.TvKey => (T)(object)"key",
            _ => default,
        };
        public void Remove(string key) { }
    }

    private sealed class DemoSettings : ISettingsStore
    {
        public UserSettings Load() => new();
        public void Save(UserSettings settings) { }
    }

    private sealed class DemoSleep : ISleepGuard
    {
        public void Begin(bool keepDisplayOn) { }
        public void End() { }
    }
}
