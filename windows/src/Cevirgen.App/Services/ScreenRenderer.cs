// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cevirgen.App.Theme;
using Cevirgen.App.Views;
using Cevirgen.Core;
using Cevirgen.Core.LgTv;
using Cevirgen.RemotePlay;
using Cevirgen.Shell;

namespace Cevirgen.App.Services;

/// <summary>
/// Geliştirme aracı: <c>Cevirgen.exe --render-screens klasör</c> bütün ekranları açık ve koyu temada PNG'ye
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
        // Kullanıcının gerçek son karesi okunmaz/yazılmaz (görüntüler her makinede aynı çıksın).
        LastFrameStore.Disabled = true;
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
            // Tanıtım görselleri için sahnenin kendisi (Çevirgen'in gece şehri çizimi; 3840 × 2160).
            if (dark) Save(() => new System.Windows.Controls.Border { Background = Controls.PerdeBrushes.Scene() }, 1920, 1080, Path.Combine(folder, "sahne.png"));
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
            Save(() => new HistoryPage(Demo(OnboardingStep.Done)), Width, Height, Path.Combine(folder, $"gecmis-{suffix}.png"));
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
            // 2. bölüm: Görünüm (varsayılan Sinema, Sade ve sabit konumlu Okunaklı), Çeviri (kör test: anahtarsız, oylarla, motorlar
            // açık ve ileri ayarlar), Geçmiş ve sözlük (oturum, satır içi düzeltme açık), Alanları düzenle (son kare ve iki kutu;
            // kare yokken yer tutucu ve kısayollu ek alan seçili).
            Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Appearance)), Width, Height, Path.Combine(folder, $"pencere-gorunum-{suffix}.png"));
            Save(() => Detach(Shell(WithStyle(Demo(OnboardingStep.Done), style => AppearancePresets.Apply(AppearancePreset.Clean, style)), ShellTab.Appearance)),
                Width, Height, Path.Combine(folder, $"pencere-gorunum-sade-{suffix}.png"));
            Save(() => Detach(Shell(WithStyle(Demo(OnboardingStep.Done), style => AppearancePresets.Apply(AppearancePreset.Classic, style)), ShellTab.Appearance)),
                Width, Height, Path.Combine(folder, $"pencere-gorunum-klasik-{suffix}.png"));
            Save(() => Detach(Shell(WithStyle(Demo(OnboardingStep.Done), style => AppearancePresets.Apply(AppearancePreset.Readable, style) with
                { Placement = SubtitlePlacement.Fixed, BottomPercent = 12, TextHex = "#FFE135", TextColor = SubtitleTextColor.Yellow }), ShellTab.Appearance)),
                Width, Height, Path.Combine(folder, $"pencere-gorunum-okunakli-{suffix}.png"));
            foreach (var (name, state, setup) in TranslationStates())
            {
                Save(() =>
                {
                    TranslationPage.Preview = state;
                    return Detach(Shell(setup(Demo(OnboardingStep.Done, keys: state.Columns.Count > 1)), ShellTab.Translation));
                }, Width, Height, Path.Combine(folder, $"pencere-ceviri{name}-{suffix}.png"));
                DemoTranslation.Installed = true;
            }
            // Uzun pencere: sayfanın altı (motor seçimi, kurulum, ileri ayarlar; Görünüm'ün "Diğer" ayarları) da görünsün.
            Save(() =>
            {
                TranslationPage.Preview = TranslationStates().ElementAt(2).State;
                return Detach(Shell(Demo(OnboardingStep.Done, keys: true), ShellTab.Translation));
            }, Width, 1900, Path.Combine(folder, $"pencere-ceviri-uzun-{suffix}.png"));
            TranslationPage.Preview = null;
            Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Appearance)), Width, 1300, Path.Combine(folder, $"pencere-gorunum-uzun-{suffix}.png"));
            Save(() => Detach(Shell(Running(), ShellTab.History)), Width, Height, Path.Combine(folder, $"pencere-gecmis-{suffix}.png"));
            Save(() =>
            {
                var controller = Running();
                var edited = controller.History.First(entry => entry.Source.StartsWith("Dude", StringComparison.Ordinal));
                controller.RemoveCorrection(edited.Source);
                HistoryPage.PreviewEdit = (edited.Id, "Kanka, kıl payı kurtulduk.");
                return Detach(Shell(controller, ShellTab.History));
            }, Width, Height, Path.Combine(folder, $"pencere-gecmis-duzelt-{suffix}.png"));
            HistoryPage.PreviewEdit = null;
            Save(() => new AreaEditorView(WithHint(Running()), () => { }), Width, Height, Path.Combine(folder, $"pencere-alanlar-{suffix}.png"));
            Save(() =>
            {
                var controller = WithHint(Demo(OnboardingStep.Done));
                controller.Settings = controller.Settings.WithExtraArea("hint", area => area with { Enabled = false });
                AreaEditorView.PreviewSelected = "hint";
                return new AreaEditorView(controller, () => { });
            }, Width, Height, Path.Combine(folder, $"pencere-alanlar-bos-{suffix}.png"));
            AreaEditorView.PreviewSelected = null;
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
            // Çevirgen 1.3: yaygın ekran boylarında taşma / kırpılma denetimi (1280 × 800 ve 1920 × 1080).
            foreach (var (w, h) in new[] { (1280, 800), (1920, 1080) })
            {
                Save(() => Detach(Live(Running())), w, h, Path.Combine(folder, $"boy-{w}-canli-{suffix}.png"));
                Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Home)), w, h, Path.Combine(folder, $"boy-{w}-canli-bos-{suffix}.png"));
                Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Appearance)), w, h, Path.Combine(folder, $"boy-{w}-gorunum-{suffix}.png"));
                Save(() =>
                {
                    TranslationPage.Preview = TranslationStates().ElementAt(1).State;
                    return Detach(Shell(Demo(OnboardingStep.Done, keys: true), ShellTab.Translation));
                }, w, h, Path.Combine(folder, $"boy-{w}-ceviri-{suffix}.png"));
                TranslationPage.Preview = null;
                Save(() => Detach(Shell(Running(), ShellTab.History)), w, h, Path.Combine(folder, $"boy-{w}-gecmis-{suffix}.png"));
                Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Settings, SettingsGroup.Devices)), w, h, Path.Combine(folder, $"boy-{w}-ayarlar-{suffix}.png"));
                Save(() => new OnboardingView(Demo(OnboardingStep.PsnLogin)), w, h, Path.Combine(folder, $"boy-{w}-kurulum-{suffix}.png"));
            }
            RenderV14(folder, suffix);
            RenderV15(folder, suffix);
        }
        app.Shutdown();
    }

    /// <summary>
    /// Çevirgen 1.4: Canlı'nın bildirim şeridi (0, 1 ve 3 bildirim; diğerleri açık), destek alanı (varsayılan ve sunucudan simgeli),
    /// Ayarlar'ın sabit alt bilgisi, oyun seçicisi (kapaksız ve kapaklı) ve anahtarsız platform sorusu; 1280 × 800 ve 1920 × 1080.
    /// </summary>
    private static void RenderV14(string folder, string suffix)
    {
        var art = Path.Combine(Path.GetTempPath(), "cevirgen-render-art");
        foreach (var (w, h) in new[] { (1280, 800), (1920, 1080) })
        {
            Save(() => Detach(Live(Running())), w, h, Path.Combine(folder, $"v14-canli-0-{w}-{suffix}.png"));
            Save(() =>
            {
                var controller = Running();
                SetPrivate(controller, nameof(AppController.TvClients), 0);
                return Detach(Live(controller));
            }, w, h, Path.Combine(folder, $"v14-canli-1-{w}-{suffix}.png"));
            foreach (var open in new[] { false, true })
                Save(() =>
                {
                    var controller = Running();
                    SetPrivate(controller, nameof(AppController.TvClients), 0);
                    SetPrivate(controller, nameof(AppController.OcrMissing), true);
                    SetPrivate(controller, nameof(AppController.GpuFallbackWarning), true);
                    NoticeStrip.PreviewShowAll = open;
                    return Detach(Live(controller));
                }, w, h, Path.Combine(folder, $"v14-canli-3{(open ? "-acik" : "")}-{w}-{suffix}.png"));
            NoticeStrip.PreviewShowAll = false;
            Save(() => Detach(Live(WithSupport(Running(), null))), w, h, Path.Combine(folder, $"v14-canli-destek-{w}-{suffix}.png"));
            Save(() =>
            {
                var icon = FakeArt(art, "destek-simge", 64, 64, Color.FromRgb(0xFF, 0xD2, 0x3F), "★");
                var support = new ResolvedSupport(new RemoteSupport("kasim", "Çevirgen'i beğendiysen bir arkadaşına da anlat; TV uygulaması da hazır.",
                    "Tanıtım sayfası", new Uri("https://cevirgen.ozgurguler.tech/"), new Uri("https://cevirgen.ozgurguler.tech/v1/assets/destek.png")), icon);
                return Detach(Live(WithSupport(Running(), support)));
            }, w, h, Path.Combine(folder, $"v14-canli-destek-simge-{w}-{suffix}.png"));
            Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Settings, SettingsGroup.Devices)), w, h, Path.Combine(folder, $"v14-ayarlar-{w}-{suffix}.png"));
            foreach (var covers in new[] { false, true })
                Save(() =>
                {
                    var controller = Running();
                    DemoStore.Recent = ["Grand Theft Auto VI", "Red Dead Redemption 2", "Ghost of Yōtei"];
                    controller.UserCover = _ => null;
                    if (covers) WithRemoteGames(controller, art);
                    var window = Live(controller);
                    controller.AskGame();
                    return Detach(window);
                }, w, h, Path.Combine(folder, $"v14-oyun-{(covers ? "kapakli" : "kapaksiz")}-{w}-{suffix}.png"));
            DemoStore.Recent = null;
        }
        Save(() =>
        {
            var controller = Demo(OnboardingStep.Welcome);
            controller.ChoosePlatform(SetupPlatform.Ps5);
            return new OnboardingView(controller);
        }, Width, Height, Path.Combine(folder, $"v14-kurulum-platform-{suffix}.png"));
    }

    /// <summary>
    /// Çevirgen 1.5: Ayarlar › Uygulama ("Çevirileri iyileştirmeye katkı" ve geliştiricinin iletişimi), Gelişmiş › Tanılama kaydı
    /// (kapalı ve açık; uzun pencere), Cihazlar'daki PS5 bildirim satırı, Canlı'da PS5 Uzaktan Oynatma bildirimi ipucu, kurulumun
    /// PS5 adımının son maddesi ve Hakkında penceresi.
    /// </summary>
    private static void RenderV15(string folder, string suffix)
    {
        Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Settings, SettingsGroup.App)), Width, 1400, Path.Combine(folder, $"v15-ayarlar-uygulama-{suffix}.png"));
        foreach (var on in new[] { false, true })
            Save(() =>
            {
                var controller = Demo(OnboardingStep.Done);
                controller.DiagnosticsOn = on;
                return Detach(Shell(controller, ShellTab.Settings, SettingsGroup.Advanced));
            }, Width, 1500, Path.Combine(folder, $"v15-ayarlar-gelismis{(on ? "-kayit-acik" : "")}-{suffix}.png"));
        Save(() => Detach(Shell(Demo(OnboardingStep.Done), ShellTab.Settings, SettingsGroup.Devices)), Width, 1200, Path.Combine(folder, $"v15-ayarlar-cihazlar-{suffix}.png"));
        foreach (var (w, h) in new[] { (Width, Height), (1920, 1080) })
            Save(() =>
            {
                var controller = Running();
                typeof(AppController).GetMethod("OnRemotePlayNoticeSeen", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                return Detach(Live(controller));
            }, w, h, Path.Combine(folder, $"v15-canli-uzaktan-{w}-{suffix}.png"));
        Save(() => new OnboardingView(Demo(OnboardingStep.Console)), Width, 1700, Path.Combine(folder, $"v15-kurulum-ps5-{suffix}.png"));
        Save(() => Detach(new AboutWindow()), 460, 900, Path.Combine(folder, $"v15-hakkinda-{suffix}.png"));
    }

    /// <summary>Destek alanı açık (20 satır çevrilmiş); <paramref name="support"/> verilirse sunucudan gelmiş gibi.</summary>
    private static AppController WithSupport(AppController controller, ResolvedSupport? support)
    {
        typeof(AppController).GetField("supportLines", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, (int?)SupportSlot.LinesBeforeShowing);
        if (support is not null) SetRemote(controller, RemoteContent.Empty with { Support = support });
        return controller;
    }

    /// <summary>
    /// Sunucudan oyun listesi gelmiş gibi; kapaklar yalnız bu araçta çizilen soyut yer tutucular (gerçek kapak indirilmez ya da
    /// gömülmez). Birinin kapağı yok (yazı karosu).
    /// </summary>
    private static void WithRemoteGames(AppController controller, string art)
    {
        (string Id, string Title, Color Tone, string[] Aliases, bool Cover)[] games =
        [
            ("gta-6", "Grand Theft Auto VI", Color.FromRgb(0xC8, 0x4B, 0x8C), ["GTA 6", "GTA VI"], true),
            ("rdr-2", "Red Dead Redemption 2", Color.FromRgb(0xA8, 0x3A, 0x2A), ["RDR2"], true),
            ("ghost-of-yotei", "Ghost of Yōtei", Color.FromRgb(0x2E, 0x5A, 0x7A), [], true),
            ("spider-man-2", "Marvel's Spider-Man 2", Color.FromRgb(0x8A, 0x1E, 0x24), [], true),
            ("death-stranding-2", "Death Stranding 2: On the Beach", Color.FromRgb(0x56, 0x6B, 0x74), ["DS2"], false),
            ("elden-ring", "Elden Ring", Color.FromRgb(0x8C, 0x74, 0x3A), [], true),
        ];
        var resolved = games.Select(g => new ResolvedGame(
            new RemoteGame(g.Id, g.Title, g.Cover ? new Uri($"https://cevirgen.ozgurguler.tech/v1/assets/{g.Id}.jpg") : null, g.Aliases),
            g.Cover ? FakeArt(art, g.Id, 248, 372, g.Tone, null) : null)).ToList();
        SetRemote(controller, RemoteContent.Empty with { Games = resolved });
    }

    private static void SetRemote(AppController controller, RemoteContent content) =>
        typeof(AppController).GetField("remoteContent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, content);

    /// <summary>Soyut yer tutucu görsel (düz renk alanları ve ufuk çizgisi): kapak ve simge düzenini denemek için.</summary>
    private static string FakeArt(string folder, string name, int width, int height, Color tone, string? glyph)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".png");
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            Color Shade(double k) => Color.FromRgb((byte)(tone.R * k), (byte)(tone.G * k), (byte)(tone.B * k));
            context.DrawRectangle(new SolidColorBrush(Shade(0.25)), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new SolidColorBrush(tone), null, new Rect(0, 0, width, height * 0.58));
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xE8, 0xB0)), null, new Point(width * 0.68, height * 0.36), width * 0.16, width * 0.16);
            context.DrawRectangle(new SolidColorBrush(Shade(0.45)), null, new Rect(0, height * 0.5, width, height * 0.12));
            for (var i = 0; i < 7; i++)
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x12, 0x18)), null,
                    new Rect(width * (0.04 + i * 0.14), height * (0.38 + (i % 3) * 0.05), width * 0.1, height * 0.3));
            if (glyph is not null)
            {
                var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"),
                    width * 0.7, Brushes.Black, 1);
                context.DrawRectangle(new SolidColorBrush(tone), null, new Rect(0, 0, width, height));
                context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        return path;
    }

    /// <summary>Ayarlar'ın bölümleri ve dosya adları.</summary>
    private static readonly (SettingsGroup Group, string Name)[] SettingsPages =
    [
        (SettingsGroup.Devices, "cihazlar"), (SettingsGroup.Areas, "alanlar"), (SettingsGroup.Dubbing, "seslendirme"),
        (SettingsGroup.Shortcuts, "kisayollar"), (SettingsGroup.Advanced, "gelismis"), (SettingsGroup.App, "uygulama"),
    ];

    /// <summary>Bir ekran görüntüsünde çizilen PNG sayısı (iki tema): sürekli tümleştirme en az bu kadarını bekler.</summary>
    public const int ExpectedCount = 194;

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
            Box: new Cevirgen.Core.TextBox(0.27, 0.765, 0.73, 0.885), Above: ["Yedek anahtarın fazlası olmaz, değil mi?"]));
        return window;
    }

    /// <summary>
    /// Çeviri sürüyormuş gibi örnek oturum: son kare, oturumun başı, kaynakları ve okuma hızlarıyla döküm, oynanan oyun.
    /// Örnek replikler bu araç için yazılmış özgün cümlelerdir (hiçbir oyundan alınmadı); kare de Çevirgen'in kendi çizimidir.
    /// Depodaki ve tanıtımdaki görseller buradan üretilir.
    /// </summary>
    private static AppController Running()
    {
        var controller = Demo(OnboardingStep.Done);
        var history = (SubtitleHistory)typeof(AppController).GetField("history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        var start = DateTime.UtcNow.AddMinutes(-42).AddSeconds(-20);
        typeof(AppController).GetField("sessionStartedAt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, start);
        var entries = (List<SubtitleHistory.Entry>)typeof(SubtitleHistory).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(history)!;
        // Dünkü oturum (Geçmiş ekranındaki oturum seçicisi için).
        var yesterday = start.AddDays(-1).AddHours(-1);
        foreach (var (seconds, source, translation) in new[]
                 {
                     (12.0, "Wait for me!", "Beni bekle!"), (15.5, "We have to go. Right now.", "Gitmemiz lazım. Hemen şimdi."),
                     (21.2, "Stay low and keep quiet.", "Eğil ve sessiz ol."),
                 })
        {
            var at = yesterday.AddSeconds(seconds);
            history.Add(source, translation, LineOrigin.Engine, at);
            entries[0] = entries[0] with { LeftAt = at.AddSeconds(2) };
        }
        foreach (var (seconds, source, translation, origin, cps) in new (double, string, string, LineOrigin, double?)[]
                 {
                     (2518.903, "Good to have you back in town, buddy.", "Şehre dönmen iyi oldu, kanka.", LineOrigin.Engine, 12.4),
                     (2522.540, "Are you selling that old boat over there?", "Şuradaki eski tekneyi satıyor musunuz?", LineOrigin.Engine, 11.8),
                     (2524.870, "Cheers!", "Şerefe!", LineOrigin.Correction, 6.8),
                     (2526.218, "I'm thinking of taking a little road trip along the coast with an old friend of mine.",
                         "Eski bir arkadaşımla kıyı boyunca ufak bir yolculuğa çıkmayı düşünüyorum.", LineOrigin.Engine, 21.2),
                     (2529.402, "Dude, that was way too close.", "Kanka, kıl payı kurtulduk.", LineOrigin.Correction, 9.6),
                     (2533.154, "You can never have too many spare keys, right?", "Yedek anahtarın fazlası olmaz, değil mi?", LineOrigin.Engine, 13.1),
                     (2535.880, "Is he still here?", "Hâlâ burada mı?", LineOrigin.Engine, null),
                 })
        {
            var at = start.AddSeconds(seconds);
            history.Add(source, translation, origin, at);
            if (cps is { } speed)
            {
                // Okuma hızı ve kalkış anı satır ekrandan kalkınca yazılır (MarkLeft); örnekte doğrudan.
                var index = entries.FindIndex(entry => entry.Source == source);
                var visible = ReadingSpeed.Characters(translation) / speed;
                if (index >= 0) entries[index] = entries[index] with { ShownAt = at, Cps = speed, LeftAt = at.AddSeconds(visible) };
            }
        }
        // Kullanıcının düzeltmeleri ve sözlüğü (bellekte; örnek denetleyicinin veri klasörü yok).
        controller.SaveCorrection("Dude, that was way too close.", "Kanka, kıl payı kurtulduk.");
        controller.SaveCorrection("Cheers!", "Şerefe!");
        controller.SaveGlossaryTerm("Port Lumen", GlossaryRule.Keep);
        controller.SaveGlossaryTerm("Azura", GlossaryRule.Keep);
        controller.SaveGlossaryTerm("buddy", GlossaryRule.Always, "kanka");
        controller.SaveGlossaryTerm("cop", GlossaryRule.Always, "aynasız");
        controller.SaveGlossaryTerm("safehouse", GlossaryRule.Always, "güvenli ev");
        typeof(AppController).GetProperty(nameof(AppController.IsRunning))!.GetSetMethod(nonPublic: true)!.Invoke(controller, [true]);
        // Görüntü geliyor ve TV uygulaması bağlı (durum noktaları yeşil).
        SetPrivate(controller, nameof(AppController.SessionState), SessionState.Streaming);
        SetPrivate(controller, nameof(AppController.TvClients), 1);
        SetPrivate(controller, nameof(AppController.LastTranslation), "Hâlâ burada mı?");
        SetPrivate(controller, nameof(AppController.PlayedGame), "Grand Theft Auto VI");
        SetPrivate(controller, nameof(AppController.LastFrame),
            DemoFrameRenderer.Draw(["You can never have too many spare keys, right?", "Is he still here?"], 0));
        return controller;
    }

    /// <summary>Altyazı biçimi değiştirilmiş örnek denetleyici.</summary>
    private static AppController WithStyle(AppController controller, Func<SubtitleStyle, SubtitleStyle> change)
    {
        controller.Settings = controller.Settings with { Style = change(controller.Settings.Style) };
        return controller;
    }

    /// <summary>Sol üstteki ipucu kutusu ek alan olarak (Alanları düzenle taslağındaki ikinci kutu).</summary>
    private static AppController WithHint(AppController controller)
    {
        controller.Settings = controller.Settings with
        {
            ExtraAreas = [new ExtraArea("hint", "İpucu", new CropRegion(0.025, 0.165, 0.02, 0.30))],
            Region = new CropRegion(0.64, 0.97, 0.14, 0.86),
        };
        return controller;
    }

    /// <summary>
    /// Çeviri sekmesinin durumları (taslak Translation.dc.html'deki çeviriler; gerçek motor çağrılmaz): yalnız ücretsiz model
    /// hazır (karşılaştırma için ikinci motor yok), üç motor karşılaştırılmış ve oylar verilmiş, motorlar açık (ileri ayarlar,
    /// bağlantı sonucu), özel OpenAI uyumlu uç noktanın kurulumu ve ücretsiz modelin indirilmesi gereken hâl.
    /// </summary>
    private static IEnumerable<(string Name, TranslationPage.PreviewState State, Func<AppController, AppController> Setup)> TranslationStates()
    {
        // BlindTest.Lines sırasıyla (özgün örnek cümleler); motorların farkı görünsün diye üç ayrı Türkçe.
        string?[] free = ["Dostum, bu çok yakındı.", "Bunu bizden başka kimse düzeltmeyecek.", "Neredeyse orada, sadece bekle.", "Burada silahı atlamayalım.", "Seni şehirde geri görmek güzel, dostum."];
        string?[] gemini = ["Kanka, kıl payı kurtulduk.", "Bu işi bizden başkası çözmez.", "Az kaldı, dayan biraz.", "Acele edip işi bozmayalım.", "Şehre dönmen iyi oldu, kanka."];
        string?[] deepl = ["Ahbap, bu fazla yakındı.", "Bunu bizden başka kimse onaramaz.", "Neredeyse vardık, biraz dayan.", "Burada aceleci davranmayalım.", "Tekrar şehirde olman güzel, dostum."];
        var results = new Dictionary<TranslationEngine, BlindTestResult>
        {
            [TranslationEngine.Free] = new(free, 62), [TranslationEngine.Gemini] = new(gemini, 248), [TranslationEngine.DeepL] = new(deepl, 305),
        };
        var none = new Dictionary<TranslationEngine, BlindTestResult>();
        BlindTestColumn[] all = [new(TranslationEngine.Gemini, true), new(TranslationEngine.Free, true), new(TranslationEngine.DeepL, true)];
        BlindTestColumn[] keyless = [new(TranslationEngine.Free, true)];
        BlindTestColumn[] online = [new(TranslationEngine.DeepL, true), new(TranslationEngine.Gemini, true)];
        Func<AppController, AppController> same = controller => controller;
        yield return ("-anahtarsiz", new TranslationPage.PreviewState(keyless, none, new Dictionary<int, int>(), Revealed: false), same);
        yield return ("", new TranslationPage.PreviewState(all, results, new Dictionary<int, int> { [0] = 0, [1] = 0, [3] = 0, [2] = 2 }, Revealed: false), same);
        yield return ("-motorlar", new TranslationPage.PreviewState(all, results, new Dictionary<int, int> { [0] = 0, [1] = 0, [3] = 0, [2] = 2, [4] = 0 }, Revealed: true,
            AdvancedOpen: true, TestResult: "Çalışıyor · 240 ms", Setup: TranslationEngine.Gemini), same);
        yield return ("-ozel", new TranslationPage.PreviewState(all, none, new Dictionary<int, int>(), Revealed: false,
            TestResult: "Çalışıyor · 412 ms", Setup: TranslationEngine.Custom), controller =>
        {
            controller.Settings = controller.Settings with { CustomAiBaseUrl = "https://openrouter.ai/api/v1", CustomAiModel = "google/gemma-3-27b-it" };
            return controller;
        });
        yield return ("-indir", new TranslationPage.PreviewState(online, none, new Dictionary<int, int>(), Revealed: false, Setup: TranslationEngine.Free), controller =>
        {
            DemoTranslation.Installed = false;
            return controller;
        });
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

    /// <param name="keys">Gemini ve DeepL anahtarı kayıtlıymış gibi (Çeviri ekranı).</param>
    internal static AppController Demo(OnboardingStep step, LgTv? pairedTv = null, bool keys = false)
    {
        var controller = new AppController(new DemoServices(), new DemoStore(step == OnboardingStep.Done, pairedTv, keys), new DemoSettings(), new DemoSleep(),
            Localizer.Current, action => action());
        if (step != OnboardingStep.Done) controller.Step = step;
        return controller;
    }

    /// <summary>
    /// Yalnız adında bu parçalardan biri geçen ekranlar çizilir (ortam değişkeni CEVIRGEN_RENDER_ONLY, virgülle; geliştirirken
    /// hız için). Boşsa hepsi.
    /// </summary>
    private static readonly string[] Only = (Environment.GetEnvironmentVariable("CEVIRGEN_RENDER_ONLY") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void Save(Func<FrameworkElement> make, int width, int height, string path)
    {
        if (Only.Length > 0 && !Only.Any(part => Path.GetFileName(path).Contains(part, StringComparison.OrdinalIgnoreCase))) return;
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
        // Gerçek uygulamada Xbox bağlantısı vardır (LiveServices); platform sorusunda dört seçenek de seçilebilir görünür.
        public IXboxService? Xbox { get; } = new DemoXbox();
        public ITranslationModel Translation { get; } = new DemoTranslation();
        public Task<IReadOnlyList<ConsoleInfo>> DiscoverConsolesAsync() => Task.FromResult<IReadOnlyList<ConsoleInfo>>([new("192.168.1.20", "PS5-123", true)]);
        public Task<byte[]> AccountIdFromRedirectAsync(Uri redirect) => Task.FromResult(new byte[8]);
        public Task<RegisteredConsole> RegisterAsync(string host, byte[] accountId, uint pin) => throw new NotSupportedException();
        public Task<IReadOnlyList<LgTv>> DiscoverTvsAsync() => Task.FromResult<IReadOnlyList<LgTv>>([new("Android TV", "192.168.1.30", TvBrand.AndroidTv)]);
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

    /// <summary>Ekran görüntüleri için Xbox hizmeti (hiçbir yere bağlanmaz).</summary>
    private sealed class DemoXbox : IXboxService
    {
        public event Action<string>? TokenUpdated { add { } remove { } }
        public void Clear() { }
        public Task<XboxLoginChallenge> BeginLoginAsync() => throw new NotSupportedException();
        public Task<string> CompleteLoginAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<XboxConsole>> DiscoverAsync(string token) => Task.FromResult<IReadOnlyList<XboxConsole>>([]);
        public IStreamSession MakeStream(XboxConsole console, string token, bool legacyFrameRate, bool playAudio) => throw new NotSupportedException();
    }

    private sealed class DemoTranslation : ITranslationModel
    {
        /// <summary>Ücretsiz model inmiş gibi (Çeviri sekmesinin "Modeli indir" hâli için false).</summary>
        public static bool Installed { get; set; } = true;
        public bool IsSupported(string source, string target) => source == "en";
        public bool IsInstalled(string source, string target) => Installed;
        public long DownloadBytes => 474_595_937;
        public Task DownloadAsync(string source, string target, IProgress<double> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public ITextTranslator Create(string source, string target) => throw new NotSupportedException();
    }

    private sealed class DemoStore(bool ready, LgTv? pairedTv = null, bool keys = false) : IAppStore
    {
        /// <summary>Oyun seçicisinin "son oynanan" karoları (yalnız istenen görüntülerde).</summary>
        public static List<string>? Recent { get; set; }

        public void Save<T>(string key, T value) { }
        public T? Load<T>(string key) => key == StoreKeys.RecentGames && Recent is { } recent ? (T)(object)recent : keys && key is StoreKeys.GeminiKey or StoreKeys.DeepLKey ? (T)(object)"demo-key"
            : pairedTv is not null && key is StoreKeys.Tv or StoreKeys.TvKey
            ? (T)(key == StoreKeys.Tv ? pairedTv : (object)"key")
            : !ready ? default : key switch
        {
            StoreKeys.AccountId => (T)(object)new byte[8],
            StoreKeys.Console => (T)(object)new RegisteredConsole("192.168.1.20", "PS5-123", new byte[16], new byte[16]),
            StoreKeys.Tv => (T)(object)new LgTv("Android TV", "192.168.1.30", TvBrand.AndroidTv),
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
