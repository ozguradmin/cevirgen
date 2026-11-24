// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Cevirgen.App.Controls;
using Cevirgen.App.Theme;
using Cevirgen.Core;
using Cevirgen.Shell;

namespace Cevirgen.App.Views;

/// <summary>
/// Çeviri sekmesi. Önce şu anki motor ve kurulumu: tek sütunlu motor listesi (kıl çizgili satırlar; durum noktası tek renk
/// modelinden), seçilen satırın altında kurulumu (anahtar sayfası → anahtar → "Bağlantıyı dene"; yerel sunucu ve "Özel
/// (OpenAI uyumlu)" uç noktada adres ve model). Sonra "İleri ayarlar" ve en altta isteğe bağlı "Motorları karşılaştır" (kör
/// test): yalnız hazır motorlar karşılaştırılır ve yalnız "Karşılaştır"a basınca çeviri istenir (sekmeyi açmak istek
/// göndermez). İndirilmesi gereken ücretsiz model sütun olmaz; "Modeli indir · 475 MB" düğmesiyle gösterilir. Kullanıcıya
/// ham hata metni gösterilmez (günlüğe yazılır).
/// </summary>
public sealed class TranslationPage : UserControl
{
    /// <summary>Ekran görüntüsü aracı için hazır durum (gerçek motor çağrılmaz).</summary>
    internal sealed record PreviewState(IReadOnlyList<BlindTestColumn> Columns, IReadOnlyDictionary<TranslationEngine, BlindTestResult> Results,
        IReadOnlyDictionary<int, int> Picks, bool Revealed, bool AdvancedOpen = false, string? TestResult = null, TranslationEngine? Setup = null,
        bool TestOk = true);

    internal static PreviewState? Preview { get; set; }

    /// <summary>Bu çalışmadaki son karşılaştırma (sekme yeniden açılınca çeviriler yeniden istenmez).</summary>
    private static IReadOnlyList<BlindTestColumn>? lastColumns;
    private static readonly Dictionary<TranslationEngine, BlindTestResult> LastResults = [];

    private static readonly TranslationEngine[] Engines =
    [
        TranslationEngine.Free, TranslationEngine.Gemini, TranslationEngine.DeepL, TranslationEngine.OpenAi, TranslationEngine.Claude,
        TranslationEngine.Custom, TranslationEngine.Local,
    ];

    /// <summary>Metin ağırlıklı içeriğin en geniş satırı (okunur genişlik).</summary>
    private const double ReadableWidth = 760;
    /// <summary>Adres, model ve anahtar alanlarının genişliği.</summary>
    private const double FieldWidth = 480;

    private readonly AppController controller;
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly StackPanel engineList = new() { Width = ReadableWidth, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel compare = new();
    private readonly StackPanel advanced = new() { Width = ReadableWidth, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button advancedToggle;
    private readonly BlindTestVotes votes = new();
    private readonly Dictionary<TranslationEngine, BlindTestResult> results = [];
    private readonly HashSet<TranslationEngine> running = [];
    private IReadOnlyList<BlindTestColumn> columns;
    private bool revealed;
    /// <summary>Kurulumu açık motor (seçili motor; listede tıklanan).</summary>
    private TranslationEngine setupEngine;
    private FrameworkElement? setupAnchor;
    private bool advancedOpen;
    private string? testResult;
    private bool testOk;
    private PasswordBox? keyBox;

    public TranslationPage(AppController controller)
    {
        this.controller = controller;
        DataContext = controller;
        controller.NoteScreen("translation");
        setupEngine = controller.Settings.Engine;
        advancedToggle = ScreenKit.Button("İleri ayarlar", "TextLink", (_, _) => { advancedOpen = !advancedOpen; ShowAdvanced(); });

        if (Preview is { } preview)
        {
            columns = preview.Columns;
            foreach (var (engine, result) in preview.Results) results[engine] = result;
            foreach (var (row, column) in preview.Picks) votes.Pick(row, column);
            revealed = preview.Revealed;
            advancedOpen = preview.AdvancedOpen;
            testResult = preview.TestResult;
            testOk = preview.TestOk;
            setupEngine = preview.Setup ?? setupEngine;
        }
        else
        {
            // Sütunlar aynı motorlarsa son karşılaştırmanın sırası ve çevirileri korunur (yeni istek gönderilmez).
            var wanted = controller.BlindTestColumns();
            columns = lastColumns is { } last && last.OrderBy(c => c.Engine).SequenceEqual(wanted.OrderBy(c => c.Engine))
                ? last : BlindTest.Shuffle(wanted, new Random());
            lastColumns = columns;
            foreach (var column in columns)
                if (LastResults.TryGetValue(column.Engine, out var cached)) results[column.Engine] = cached;
        }

        var stack = new StackPanel { Margin = Sp.T("edge", "l", "edge", "xl") };
        stack.Children.Add(ScreenKit.Title("Çeviri"));
        var lead = ScreenKit.Muted("Altyazılar seçtiğin motorla çevrilir. Anahtarsız da çalışır: ücretsiz model bu bilgisayarda çevirir, yazılar dışarı çıkmaz. Daha doğal çeviri için kendi anahtarınla bir motor kur; anahtar yalnız bu bilgisayarda, şifreli saklanır.", 15);
        lead.MaxWidth = ReadableWidth;
        lead.HorizontalAlignment = HorizontalAlignment.Left;
        lead.Margin = Sp.T("0", "xs", "0", "l");
        stack.Children.Add(lead);

        stack.Children.Add(SectionTitle("Çeviri motoru"));
        AutomationProperties.SetName(engineList, "Çeviri motorları");
        stack.Children.Add(engineList);

        advancedToggle.Margin = Sp.T("0", "l", "0", "0");
        advancedToggle.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(advancedToggle);
        advanced.Margin = Sp.T("0", "s", "0", "0");
        stack.Children.Add(advanced);

        compare.Margin = Sp.T("0", "xl", "0", "0");
        stack.Children.Add(compare);
        scroll.Content = stack;
        Content = scroll;

        controller.PropertyChanged += OnChanged;
        Unloaded += (_, _) => controller.PropertyChanged -= OnChanged;
        Loaded += (_, _) =>
        {
            controller.PropertyChanged -= OnChanged;
            controller.PropertyChanged += OnChanged;
        };
        ShowEngines();
        ShowAdvanced();
        ShowCompare();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppController.Settings) or nameof(AppController.HasDeepLKey) or nameof(AppController.UsesFreeGemini)
            or nameof(AppController.ModelInstalled) or nameof(AppController.ModelDownloading) or nameof(AppController.ModelProgress)
            or nameof(AppController.ModelError))) return;
        Dispatcher.BeginInvoke(() =>
        {
            // Yazı yazılırken satırlar yeniden kurulmaz (odak ve yazılan anahtar kaybolmasın).
            if (!IsKeyboardFocusWithin || keyBox?.IsKeyboardFocusWithin != true && !(System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox)) ShowEngines();
            if (e.PropertyName is nameof(AppController.ModelInstalled) or nameof(AppController.ModelDownloading) or nameof(AppController.ModelError))
                RefreshColumns();
            ShowCompare();
        });
    }

    private static TextBlock SectionTitle(string text)
    {
        var title = new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = Sp.T("0", "0", "0", "s") };
        title.SetResourceReference(TextBlock.ForegroundProperty, "textBrush");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        return title;
    }

    // ───── Motor listesi ve kurulum ─────

    /// <summary>Motorun listedeki durumu (tek renk modeli) ve kısa adı.</summary>
    private (DeviceState State, string Word) EngineStatus(TranslationEngine engine)
    {
        var active = engine == controller.Settings.Engine;
        if (engine == TranslationEngine.Free)
        {
            if (controller.ModelDownloading) return (DeviceState.Waiting, "iniyor");
            if (!controller.ModelInstalled) return (DeviceState.Waiting, "indirilmesi gerekiyor");
            return (DeviceState.Ready, active ? "kullanılıyor" : "hazır");
        }
        if (engine.UsesServer())
        {
            var configured = engine == TranslationEngine.Local ? controller.IsComparable(engine) : controller.IsReady(engine);
            return configured ? (DeviceState.Ready, active ? "kullanılıyor" : "ayarlı") : (DeviceState.Off, "kurulmadı");
        }
        if (!controller.HasKey(engine)) return active ? (DeviceState.Waiting, "anahtar gerekli") : (DeviceState.Off, "anahtar gerekli");
        return (DeviceState.Ready, active ? "kullanılıyor" : "hazır");
    }

    /// <summary>Tek sütunlu liste: her motor bir satır (seçim göstergesi, ad, kısa bilgi, durum); kurulumu açık motorun ayarları altında.</summary>
    private void ShowEngines()
    {
        engineList.Children.Clear();
        var current = controller.Settings.Engine;
        foreach (var engine in Engines)
        {
            var active = engine == current;
            var open = engine == setupEngine;
            var (state, word) = EngineStatus(engine);

            var indicator = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = Sp.T("0", "xxs", "s", "0") };
            var ring = new Ellipse { StrokeThickness = active ? 2 : 1.5 };
            ring.SetResourceReference(Shape.StrokeProperty, active ? "amberBrush" : "controlBorderBrush");
            indicator.Children.Add(ring);
            if (active)
            {
                var fill = new Ellipse { Width = 8, Height = 8 };
                fill.SetResourceReference(Shape.FillProperty, "amberBrush");
                indicator.Children.Add(fill);
            }

            var texts = new StackPanel();
            texts.Children.Add(ScreenKit.Text(BlindTest.EngineName(engine), 15, FontWeights.Medium, wrap: false));
            var facts = ScreenKit.Small(BlindTest.Facts(engine), 13);
            facts.Margin = Sp.T("0", "line", "0", "0");
            texts.Children.Add(facts);

            var status = DeviceStatus.Line(state, word, active && state == DeviceState.Ready ? "textBrush" : "mutedBrush");
            status.VerticalAlignment = VerticalAlignment.Top;
            status.Margin = Sp.T("s", "xxs", "0", "0");

            var row = new DockPanel();
            DockPanel.SetDock(indicator, Dock.Left);
            DockPanel.SetDock(status, Dock.Right);
            row.Children.Add(indicator);
            row.Children.Add(status);
            row.Children.Add(texts);

            var button = new Button
            {
                Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Padding = Sp.T("s", "s", "s", "s"), Cursor = System.Windows.Input.Cursors.Hand, Template = RowTemplate(),
                HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent,
            };
            button.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
            AutomationProperties.SetName(button, $"{BlindTest.EngineName(engine)}: {word}{(active ? ", seçili" : "")}");
            var chosen = engine;
            button.Click += (_, _) =>
            {
                var reopen = setupEngine != chosen;
                setupEngine = chosen;
                if (reopen) testResult = null;
                // Hazır motor hemen kullanılır; kurulmamış motorda önce kurulum (çalışınca seçilir).
                // Yerel sunucu varsayılan adresle hep "geçerli" görünür; çalıştığı "Bağlantıyı dene" ile görülünce seçilir.
                if (chosen == TranslationEngine.Free ? controller.ModelInstalled
                    : chosen != TranslationEngine.Local && controller.IsReady(chosen)) controller.UseEngine(chosen);
                ShowEngines();
                ShowAdvanced();
            };

            var item = new StackPanel();
            item.Children.Add(button);
            if (open)
            {
                var setup = Setup(engine);
                // Kurulum adın hizasında başlar (gösterge 18 + iki pay).
                setup.Margin = new Thickness(Sp.V("s") * 2 + 18, 0, Sp.V("s"), Sp.V("m"));
                item.Children.Add(setup);
                setupAnchor = item;
            }
            var frame = new Border { Child = item, BorderThickness = new Thickness(0, 1, 0, 0) };
            frame.SetResourceReference(Border.BorderBrushProperty, "hairlineBrush");
            if (open) frame.SetResourceReference(Border.BackgroundProperty, "surfaceBrush");
            engineList.Children.Add(frame);
        }
        var bottom = ScreenKit.Rule();
        bottom.SetResourceReference(Border.BackgroundProperty, "hairlineBrush");
        engineList.Children.Add(bottom);
    }

    /// <summary>Satır düğmesinin şablonu: zemin ve pay; üzerine gelince alan rengi.</summary>
    private static ControlTemplate RowTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Cell");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
        presenter.SetValue(VerticalAlignmentProperty, new TemplateBindingExtension(VerticalContentAlignmentProperty));
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("fieldBrush"), "Cell"));
        template.Triggers.Add(hover);
        return template;
    }

    private void GoToSetup(TranslationEngine engine)
    {
        setupEngine = engine;
        testResult = null;
        ShowEngines();
        ShowAdvanced();
        Dispatcher.BeginInvoke(() => setupAnchor?.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Kurulumu açık motorun ayarları: numaralı kısa adımlar, tek sarı düğme ("Bağlantıyı dene" ya da "Modeli indir").</summary>
    private FrameworkElement Setup(TranslationEngine engine)
    {
        var panel = new StackPanel();
        AutomationProperties.SetName(panel, $"{BlindTest.EngineName(engine)} kurulumu");
        if (engine == TranslationEngine.Free)
        {
            panel.Children.Add(FreeModelState(primary: true));
            return panel;
        }
        if (engine.UsesServer())
        {
            ServerSetup(panel, engine);
            return panel;
        }
        var (link, how) = engine switch
        {
            TranslationEngine.Gemini => ("Google AI Studio'yu aç ↗", "Google hesabınla gir, “Create API key”e bas, anahtarı kopyala. Ücretsiz."),
            TranslationEngine.DeepL => ("DeepL API sayfasını aç ↗", "Ücretsiz DeepL API hesabı aç, hesap sayfasından anahtarı kopyala."),
            TranslationEngine.OpenAi => ("OpenAI anahtar sayfasını aç ↗", "Hesabınla gir, “Create new secret key”e bas. Kullanım ücretlidir."),
            _ => ("Anthropic Console'u aç ↗", "Hesabınla gir, “Create Key”e bas. Kullanım ücretlidir."),
        };
        var uri = engine.Provider()?.KeyPage() ?? new Uri("https://www.deepl.com/pro-api");
        panel.Children.Add(Numbered(1, "Anahtarı al", LinkWithHint(link, how, uri)));

        var saved = controller.HasKey(engine);
        var keyArea = new StackPanel();
        if (saved)
        {
            var row = new DockPanel { Margin = Sp.T("0", "0", "0", "xs") };
            var remove = ScreenKit.Button("Kaldır", "TextLink", (_, _) =>
            {
                controller.RemoveKey(engine);
                testResult = null;
                ShowEngines();
            }, $"{BlindTest.EngineName(engine)} anahtarını kaldır");
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(DeviceStatus.Line(DeviceState.Ready, "Anahtar kayıtlı · yenisini yazarsan eskisinin yerine geçer"));
            keyArea.Children.Add(row);
        }
        keyBox = new PasswordBox { MinHeight = 36, Width = FieldWidth, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(keyBox, $"{BlindTest.EngineName(engine)} anahtarı");
        keyArea.Children.Add(keyBox);
        panel.Children.Add(Numbered(2, "Anahtarı yapıştır", keyArea));
        panel.Children.Add(Numbered(3, "Dene", TestArea(engine, "Örnek bir satır çevrilir; anahtar kabul edilirse kaydedilir ve motor seçilir.")));
        return panel;
    }

    /// <summary>Yerel sunucu (Ollama, LM Studio) ya da özel OpenAI uyumlu uç nokta: adres, model, isteğe bağlı anahtar, dene.</summary>
    private void ServerSetup(StackPanel panel, TranslationEngine engine)
    {
        var settings = controller.Settings;
        var custom = engine == TranslationEngine.Custom;
        if (custom)
            panel.Children.Add(ScreenKit.Small("OpenAI uyumlu /v1/chat/completions sunan her hizmet olur: OpenRouter, Groq, Together, DeepInfra, LM Studio, Ollama. Adresi, model adını ve (gerekiyorsa) anahtarı yaz.", 13));
        else
            panel.Children.Add(Numbered(1, "Sunucuyu kur", LinkWithHint("Ollama'yı indir ↗", "Ollama'yı kur ve bir model indir: ollama pull qwen2.5:7b. LM Studio da olur.", AiProvider.Local.KeyPage())));

        var address = new System.Windows.Controls.TextBox { Text = custom ? settings.CustomAiBaseUrl : settings.LocalAiBaseUrl, MinHeight = 36 };
        var model = new System.Windows.Controls.TextBox { Text = custom ? settings.CustomAiModel : settings.LocalAiModel, MinHeight = 36, Margin = Sp.T("0", "xs", "0", "0") };
        AutomationProperties.SetName(address, "Sunucu adresi");
        AutomationProperties.SetName(model, "Model adı");
        void Commit()
        {
            var (a, m) = (address.Text.Trim(), model.Text.Trim());
            var now = controller.Settings;
            if (custom && (a != now.CustomAiBaseUrl || m != now.CustomAiModel))
                controller.Settings = now with { CustomAiBaseUrl = a, CustomAiModel = m };
            else if (!custom && (a != now.LocalAiBaseUrl || m != now.LocalAiModel))
                controller.Settings = now with { LocalAiBaseUrl = a, LocalAiModel = m };
        }
        address.LostFocus += (_, _) => Commit();
        model.LostFocus += (_, _) => Commit();
        var fields = new StackPanel { Width = FieldWidth, HorizontalAlignment = HorizontalAlignment.Left };
        fields.Children.Add(ScreenKit.Field(address, custom ? "https://openrouter.ai/api/v1" : LocalAiServer.OllamaUrl));
        fields.Children.Add(ScreenKit.Field(model, custom ? "ör. google/gemma-3-27b-it" : LocalAiServer.DefaultModel));
        var presets = new WrapPanel { Margin = Sp.T("0", "xs", "0", "0") };
        (string Name, string Url)[] choices = custom
            ? [("OpenRouter", "https://openrouter.ai/api/v1"), ("Groq", "https://api.groq.com/openai/v1"), ("Together", "https://api.together.xyz/v1"),
               ("LM Studio", LocalAiServer.LmStudioUrl)]
            : [("Ollama", LocalAiServer.OllamaUrl), ("LM Studio", LocalAiServer.LmStudioUrl)];
        foreach (var (name, url) in choices)
        {
            var preset = ScreenKit.Button(name, "CompactButton", (_, _) => { address.Text = url; Commit(); }, $"Adres: {name}");
            preset.Margin = Sp.T("0", "0", "xs", "xs");
            presets.Children.Add(preset);
        }
        fields.Children.Add(presets);
        panel.Children.Add(Numbered(custom ? 1 : 2, "Adres ve model", fields));

        var keyArea = new StackPanel { Width = FieldWidth, HorizontalAlignment = HorizontalAlignment.Left };
        if (controller.HasKey(engine))
        {
            var row = new DockPanel { Margin = Sp.T("0", "0", "0", "xs") };
            var remove = ScreenKit.Button("Kaldır", "TextLink", (_, _) => { controller.RemoveKey(engine); testResult = null; ShowEngines(); }, "Anahtarı kaldır");
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(DeviceStatus.Line(DeviceState.Ready, "Anahtar kayıtlı"));
            keyArea.Children.Add(row);
        }
        keyBox = new PasswordBox { MinHeight = 36 };
        AutomationProperties.SetName(keyBox, "Anahtar (isteğe bağlı)");
        keyArea.Children.Add(keyBox);
        var keyHint = ScreenKit.Small(custom ? "Bulut hizmetlerinde gerekir; yerel sunucuda boş bırak. Yalnız bu bilgisayarda, şifreli saklanır." : "Çoğu yerel sunucu anahtar istemez; boş bırakabilirsin.", 12);
        keyHint.Margin = Sp.T("0", "xxs", "0", "0");
        keyArea.Children.Add(keyHint);
        panel.Children.Add(Numbered(custom ? 2 : 3, "Anahtar (isteğe bağlı)", keyArea));
        panel.Children.Add(Numbered(custom ? 3 : 4, "Dene", TestArea(engine, "Sunucuya örnek bir satır gönderilir; çalışırsa motor seçilir.", commit: Commit)));
    }

    /// <summary>Numaralı adım: solda numara, sağda başlık ve içerik.</summary>
    private static FrameworkElement Numbered(int number, string title, UIElement content)
    {
        var row = new DockPanel { Margin = Sp.T("0", "s", "0", "0") };
        var badge = ScreenKit.Mono(number.ToString(CultureInfo.InvariantCulture), 12, "mutedBrush");
        badge.Width = 20;
        badge.Margin = Sp.T("0", "line", "0", "0");
        badge.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        var body = new StackPanel();
        var heading = ScreenKit.Text(title, 14, FontWeights.SemiBold);
        heading.Margin = Sp.T("0", "0", "0", "xs");
        body.Children.Add(heading);
        body.Children.Add(content);
        row.Children.Add(body);
        return row;
    }

    private StackPanel LinkWithHint(string link, string how, Uri uri)
    {
        var block = new StackPanel();
        var open = ScreenKit.Button(link, "TextLink", (_, _) => controller.OpenInBrowser(uri));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        block.Children.Add(open);
        var hint = ScreenKit.Small(how, 13);
        hint.Margin = Sp.T("0", "xxs", "0", "0");
        block.Children.Add(hint);
        return block;
    }

    /// <summary>"Bağlantıyı dene" (sayfanın tek sarı düğmesi) ve sonucu (Türkçe cümle; yeşil çalışıyor, kırmızı sorun).</summary>
    private StackPanel TestArea(TranslationEngine engine, string hint, Action? commit = null)
    {
        var area = new StackPanel();
        var test = ScreenKit.Button("Bağlantıyı dene", "PrimaryButton", async (sender, _) =>
        {
            commit?.Invoke();
            await TestAsync((Button)sender, engine);
        });
        test.MinHeight = 40;
        test.HorizontalAlignment = HorizontalAlignment.Left;
        area.Children.Add(test);
        FrameworkElement line = testResult is { } result
            ? DeviceStatus.Line(testOk ? DeviceState.Ready : DeviceState.Error, result, testOk ? "successTextBrush" : "liveTextBrush")
            : ScreenKit.Small(hint, 12);
        if (line is StackPanel { Children: [_, TextBlock words] })
        {
            words.TextWrapping = TextWrapping.Wrap;
            words.TextTrimming = TextTrimming.None;
            words.MaxWidth = 600;
        }
        line.Margin = Sp.T("0", "xs", "0", "0");
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        area.Children.Add(line);
        return area;
    }

    /// <summary>"Bağlantıyı dene": yazılan anahtar (yoksa kayıtlı olan) örnek satırla denenir; süre ölçülür.</summary>
    private async Task TestAsync(Button button, TranslationEngine engine)
    {
        button.IsEnabled = false;
        button.Content = "Deneniyor…";
        var typed = keyBox?.Password.Trim() ?? "";
        var watch = Stopwatch.StartNew();
        try
        {
            var text = Localizer.Current;
            if (engine.UsesServer())
            {
                var result = engine == TranslationEngine.Custom ? await controller.TestCustomAiAsync(typed) : await controller.TestLocalAiAsync(typed);
                var ok = result.State == OnlineKeyState.Valid;
                if (ok && engine == TranslationEngine.Local) controller.UseEngine(engine);
                Report(ok, result.State.Describe(engine, text, controller.Settings.ServerFor(engine)), watch.ElapsedMilliseconds);
            }
            else if (engine.Provider() is { } provider)
            {
                if (typed.Length == 0 && !controller.HasKey(engine))
                {
                    Report(false, "Önce 2. adımda anahtarı yapıştır.", null);
                    return;
                }
                var result = typed.Length > 0 ? await controller.SetAiKeyAsync(provider, typed) : await controller.TestAiKeyAsync(provider);
                Report(result.State == OnlineKeyState.Valid, result.State.Describe(engine, text), watch.ElapsedMilliseconds);
            }
            else
            {
                if (typed.Length == 0 && !controller.HasKey(engine))
                {
                    Report(false, "Önce 2. adımda anahtarı yapıştır.", null);
                    return;
                }
                var check = typed.Length > 0 ? await controller.SetDeepLKeyAsync(typed) : await controller.CheckDeepLKeyAsync();
                Report(check.State is OnlineKeyState.Valid or OnlineKeyState.QuotaExceeded, check.State.Describe(engine, text), watch.ElapsedMilliseconds);
            }
        }
        finally
        {
            ShowEngines();
            ShowCompare();
        }
    }

    private void Report(bool ok, string message, long? ms)
    {
        testOk = ok;
        testResult = ok && ms is { } elapsed ? string.Create(CultureInfo.InvariantCulture, $"Çalışıyor · {elapsed} ms") : message;
        if (ok && Preview is null) RefreshColumns();
    }

    /// <summary>Hazır motorlar değiştiyse karşılaştırmanın sütunları yenilenir (çeviri istenmez; "Karşılaştır" bekler).</summary>
    private void RefreshColumns()
    {
        if (Preview is not null) return;
        var wanted = controller.BlindTestColumns();
        if (wanted.OrderBy(c => c.Engine).SequenceEqual(columns.OrderBy(c => c.Engine))) return;
        columns = BlindTest.Shuffle(wanted, new Random());
        lastColumns = columns;
        votes.Clear();
        results.Clear();
        foreach (var column in columns)
            if (LastResults.TryGetValue(column.Engine, out var cached)) results[column.Engine] = cached;
    }

    /// <summary>Ücretsiz modelin durumu: kurulu, iniyor (ilerleme) ya da "Modeli indir · 475 MB".</summary>
    private FrameworkElement FreeModelState(bool primary)
    {
        var panel = new StackPanel();
        if (controller.ModelInstalled)
        {
            panel.Children.Add(ScreenKit.Small("Model bu bilgisayarda kurulu. Çeviri çevrimdışı yapılır; altyazı yazıları hiçbir yere gönderilmez.", 13));
            return panel;
        }
        if (controller.ModelDownloading)
        {
            var percent = (int)Math.Round(controller.ModelProgress * 100);
            panel.Children.Add(ScreenKit.Small(string.Create(CultureInfo.InvariantCulture, $"Model iniyor… %{percent}"), 13));
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = controller.ModelProgress, Height = 4, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = Sp.T("0", "xs", "0", "0") };
            panel.Children.Add(bar);
            return panel;
        }
        panel.Children.Add(ScreenKit.Small("Ücretsiz model henüz bu bilgisayara inmedi. Bir kez indirilir; sonra çeviri internetsiz de çalışır.", 13));
        var size = controller.ModelDownloadBytes > 0
            ? string.Create(CultureInfo.InvariantCulture, $" · {Math.Round(controller.ModelDownloadBytes / 1_000_000d):0} MB") : "";
        var download = ScreenKit.Button("Modeli indir" + size, primary ? "PrimaryButton" : "OutlineButton", (_, _) => controller.StartModelDownload());
        download.MinHeight = primary ? 40 : 36;
        download.Margin = Sp.T("0", "s", "0", "0");
        panel.Children.Add(download);
        if (controller.ModelError is not null)
        {
            var error = DeviceStatus.Line(DeviceState.Error, "İndirilemedi. İnternet bağlantını kontrol edip yeniden dene.", "liveTextBrush");
            error.Margin = Sp.T("0", "xs", "0", "0");
            panel.Children.Add(error);
        }
        return panel;
    }

    // ───── İleri ─────

    /// <summary>İleri: model (yapay zekâ), ücretli Gemini anahtarı, çeviri hızı, oyun dili, ek talimat.</summary>
    private void ShowAdvanced()
    {
        advancedToggle.Content = advancedOpen ? "İleri ayarları gizle" : "İleri ayarlar";
        advanced.Children.Clear();
        advanced.Visibility = advancedOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!advancedOpen) return;
        var settings = controller.Settings;
        var engine = setupEngine;
        var provider = engine.Provider();

        if (provider is { } ai && ai != AiProvider.Local)
        {
            var models = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Right };
            var selected = ai.Model(settings.ModelFor(ai)).Id;
            foreach (var option in ai.Models())
                models.Items.Add(new ComboBoxItem { Content = Localizer.Current[option.Label], Tag = option.Id, IsSelected = option.Id == selected });
            models.SelectionChanged += (_, _) =>
            {
                if (models.SelectedItem is ComboBoxItem { Tag: string id }) controller.Settings = controller.Settings.WithModel(ai, id);
            };
            AutomationProperties.SetName(models, "Model");
            advanced.Children.Add(Line($"{BlindTest.EngineName(engine)} modeli", models));
        }
        if (engine == TranslationEngine.Gemini)
        {
            var paid = new SwitchRow
            {
                Content = "Ücretli Gemini anahtarı kullanıyorum",
                Description = "Ücretsiz anahtarda dakikalık sınır vardır ve Google yazıları ürünlerini geliştirmek için kullanabilir; sınıra gelinen satırı ücretsiz model çevirir.",
                IsChecked = settings.GeminiPaidTier, Divider = false,
            };
            paid.Checked += (_, _) => controller.Settings = controller.Settings with { GeminiPaidTier = true };
            paid.Unchecked += (_, _) => controller.Settings = controller.Settings with { GeminiPaidTier = false };
            advanced.Children.Add(ScreenKit.Row(paid));
        }

        var pace = TranslationPaces.Match(settings);
        var paces = TranslationPaces.All;
        var paceIndex = pace is null ? -1 : paces.ToList().IndexOf(pace);
        var paceBox = ScreenKit.Segmented("Çeviri hızı", [.. paces.Select(p => p.Name)], paceIndex,
            index => controller.Settings = TranslationPaces.Apply(controller.Settings, paces[index]), minWidth: 80);
        var paceBlock = new StackPanel();
        var paceLine = new DockPanel();
        DockPanel.SetDock(paceBox, Dock.Right);
        paceLine.Children.Add(paceBox);
        var paceName = ScreenKit.Text("Çeviri hızı", 14);
        paceName.VerticalAlignment = VerticalAlignment.Center;
        paceLine.Children.Add(paceName);
        paceBlock.Children.Add(paceLine);
        var paceHint = ScreenKit.Small(pace?.Hint ?? "Bekleme süreleri elle ayarlandı (Ayarlar › Gelişmiş).", 13);
        paceHint.Margin = Sp.T("0", "xs", "0", "0");
        paceBlock.Children.Add(paceHint);
        advanced.Children.Add(ScreenKit.Row(paceBlock));

        var languages = new[] { ("en", "İngilizce"), ("ja", "Japonca"), ("de", "Almanca"), ("fr", "Fransızca"), ("es", "İspanyolca") }
            .Where(language => controller.SupportsLanguage(language.Item1)).ToList();
        if (languages.Count > 1)
        {
            var choice = new ComboBox { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var (code, name) in languages)
                choice.Items.Add(new ComboBoxItem { Content = name, Tag = code, IsSelected = code == settings.SourceLanguage });
            choice.SelectionChanged += (_, _) =>
            {
                if (choice.SelectedItem is ComboBoxItem { Tag: string code } && code != controller.Settings.SourceLanguage)
                    controller.Settings = controller.Settings with { SourceLanguage = code };
            };
            AutomationProperties.SetName(choice, "Oyunun dili");
            advanced.Children.Add(Line("Oyunun dili", choice));
            if (settings.SourceLanguage != "en")
                advanced.Children.Add(ScreenKit.Small("Çevrimiçi motorlar yalnız İngilizce oyunları çevirir; diğer dillerde ücretsiz model çevirir.", 13));
        }
        else
        {
            advanced.Children.Add(Line("Çeviri dili", ScreenKit.Text("İngilizce → Türkçe", 14)));
        }

        if (provider is not null)
        {
            var instruction = new System.Windows.Controls.TextBox
            {
                Text = settings.AiInstruction, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 160, MaxLength = 1000,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = Sp.T("0", "xs", "0", "0"),
            };
            AutomationProperties.SetName(instruction, "Ek talimat");
            instruction.LostFocus += (_, _) =>
            {
                var text = instruction.Text.Trim();
                if (text != controller.Settings.AiInstruction) controller.Settings = controller.Settings with { AiInstruction = text };
            };
            var block = new StackPanel();
            block.Children.Add(ScreenKit.Text("Ek talimat", 14));
            block.Children.Add(ScreenKit.Field(instruction, "Örnek: Küfürleri yumuşatmadan çevir."));
            var hint = ScreenKit.Small("Yapay zekâ motorlarının talimatının sonuna eklenir (en çok 1000 karakter). Sözlükteki kurallar ayrıca eklenir.", 13);
            hint.Margin = Sp.T("0", "xs", "0", "0");
            block.Children.Add(hint);
            advanced.Children.Add(ScreenKit.Row(block));
        }
    }

    private static Border Line(string name, FrameworkElement control)
    {
        var row = new DockPanel();
        DockPanel.SetDock(control, Dock.Right);
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(control);
        var label = ScreenKit.Text(name, 14);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        return ScreenKit.Row(row);
    }

    // ───── Motorları karşılaştır (kör test) ─────

    /// <summary>
    /// İsteğe bağlı karşılaştırma: başlık, kısa açıklama ve "Karşılaştır" (çeviri yalnız basınca istenir). İki hazır motor
    /// yoksa ne yapılacağı söylenir; ücretsiz model inmediyse indirme düğmesi.
    /// </summary>
    private void ShowCompare()
    {
        compare.Children.Clear();
        compare.Children.Add(SectionTitle("Motorları karşılaştır"));
        var lead = ScreenKit.Muted("Beş zor repliği hazır motorlarına çevirtir, adlarını gizler. Her satırda kulağına en doğal geleni seç; sonra hangi motorun kazandığını gör.", 14);
        lead.MaxWidth = ReadableWidth;
        lead.HorizontalAlignment = HorizontalAlignment.Left;
        lead.Margin = Sp.T("0", "0", "0", "m");
        compare.Children.Add(lead);

        if (!controller.ModelInstalled && Preview?.Columns.Any(column => column.Engine == TranslationEngine.Free) != true)
        {
            var free = FreeModelState(primary: false);
            free.Margin = Sp.T("0", "0", "0", "m");
            free.MaxWidth = ReadableWidth;
            free.HorizontalAlignment = HorizontalAlignment.Left;
            compare.Children.Add(free);
        }

        if (!BlindTest.CanCompare(columns))
        {
            var need = ScreenKit.Small("Karşılaştırmak için en az iki hazır motor gerekir. Yukarıdaki listeden bir motor kur (Gemini ücretsiz anahtarla çalışır).", 13);
            need.MaxWidth = ReadableWidth;
            need.HorizontalAlignment = HorizontalAlignment.Left;
            compare.Children.Add(need);
            var gemini = ScreenKit.Button("Gemini'yi kur", "OutlineButton", (_, _) => GoToSetup(TranslationEngine.Gemini));
            gemini.Margin = Sp.T("0", "s", "0", "0");
            compare.Children.Add(gemini);
            return;
        }

        var actions = new WrapPanel();
        var compared = results.Count > 0 || running.Count > 0;
        var run = ScreenKit.Button(running.Count > 0 ? "Çevriliyor…" : compared ? "Yeniden çevir" : "Karşılaştır", "OutlineButton",
            async (_, _) => await RunAsync());
        run.IsEnabled = running.Count == 0;
        run.Margin = Sp.T("0", "0", "xs", "0");
        actions.Children.Add(run);
        if (compared)
        {
            var reveal = ScreenKit.Button(revealed ? "Motorları gizle" : "Motorları göster", "GhostButton", (_, _) => { revealed = !revealed; ShowCompare(); });
            reveal.MinHeight = 36;
            actions.Children.Add(reveal);
        }
        else
        {
            var names = string.Join(", ", columns.Select(column => BlindTest.EngineName(column.Engine)));
            var note = ScreenKit.Small($"{columns.Count} motor: {names}. Her motora beş satır gönderilir.", 12);
            note.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(note);
        }
        compare.Children.Add(actions);
        if (!compared) return;

        var table = new Grid();
        BuildTable(table);
        var frame = new Border { BorderThickness = new Thickness(1), Child = table, Margin = Sp.T("0", "m", "0", "0"), CornerRadius = new CornerRadius(6),
            SnapsToDevicePixels = true, ClipToBounds = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        AutomationProperties.SetName(frame, "Örnek çeviriler");
        compare.Children.Add(frame);
        var hint = ScreenKit.Small("Çeviriler bu bilgisayardan gerçek motorlarla istenir; süre satır başına ortalamadır. Seçmek için hücreye tıkla.", 12);
        hint.Margin = Sp.T("0", "xs", "0", "0");
        compare.Children.Add(hint);
        compare.Children.Add(Winner());
    }

    /// <summary>Karşılaştırma: hazır sütunların çevirileri aynı anda istenir. Önceki sonuçlar ve seçimler silinir.</summary>
    private async Task RunAsync()
    {
        if (Preview is not null || running.Count > 0) return;
        foreach (var column in columns) LastResults.Remove(column.Engine);
        results.Clear();
        votes.Clear();
        foreach (var column in columns) running.Add(column.Engine);
        ShowCompare();
        await Task.WhenAll(columns.Select(async column =>
        {
            BlindTestResult result;
            try { result = await controller.TranslateSamplesAsync(column.Engine, BlindTest.Lines); }
            catch (Exception error)
            {
                // Ham hata yalnız günlüğe; ekranda Türkçe cümle.
                SubtitleLog.Write($"kör test: {column.Engine} beklenmedik hata: {error.GetType().Name}: {error.Message}");
                result = new BlindTestResult([.. BlindTest.Lines.Select(_ => (string?)null)], null, "Bu motor şu an çeviremedi. Biraz sonra yeniden dene.");
            }
            results[column.Engine] = result;
            LastResults[column.Engine] = result;
            running.Remove(column.Engine);
            ShowCompare();
        }));
    }

    /// <summary>Tablo: replik sütunu ve motor sütunları; başlıkta ad (gizliyse Motor A/B/C) ve seçim sayısı; açılınca ücret · hız.</summary>
    private void BuildTable(Grid table)
    {
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 180 });
        foreach (var _ in columns) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = BlindTest.Lines.Count + 1 + (revealed ? 1 : 0);
        for (var r = 0; r < rows; r++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Place(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            table.Children.Add(element);
        }

        Place(Cell(ScreenKit.Label("Replik"), head: true), 0, 0);
        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            var name = ScreenKit.Text(revealed ? BlindTest.EngineName(column.Engine) : BlindTest.HiddenName(c), 14, FontWeights.SemiBold, wrap: false);
            var count = ScreenKit.Small(votes.VotesText(c), 12, votes.Votes(c) > 0 ? "amberTextBrush" : "mutedBrush");
            count.TextWrapping = TextWrapping.NoWrap;
            count.VerticalAlignment = VerticalAlignment.Center;
            var line = new DockPanel();
            DockPanel.SetDock(count, Dock.Right);
            count.Margin = Sp.T("xs", "0", "0", "0");
            line.Children.Add(count);
            line.Children.Add(name);
            Place(Cell(line, head: true), 0, c + 1);
        }

        for (var r = 0; r < BlindTest.Lines.Count; r++)
        {
            var english = ScreenKit.Muted(BlindTest.Lines[r], 14);
            english.LineHeight = 20;
            Place(Cell(english), r + 1, 0);
            for (var c = 0; c < columns.Count; c++) Place(Choice(r, c), r + 1, c + 1);
        }

        if (!revealed) return;
        var last = BlindTest.Lines.Count + 1;
        Place(Cell(ScreenKit.Label("Ücret · hız"), head: true), last, 0);
        for (var c = 0; c < columns.Count; c++)
        {
            var result = results.GetValueOrDefault(columns[c].Engine);
            var facts = ScreenKit.Small(BlindTest.FactsWithSpeed(columns[c].Engine, result?.AverageMs), 12);
            Place(Cell(facts, head: true), last, c + 1);
        }
    }

    /// <summary>Tablo hücresi: üstte kıl çizgi (ilk satırda yok); başlık hücreleri yüzey renginde.</summary>
    private static Border Cell(UIElement child, bool head = false)
    {
        var cell = new Border { Child = child, Padding = Sp.T("m", "s", "m", "s"), BorderThickness = new Thickness(0, 0, 0, 1) };
        cell.SetResourceReference(Border.BorderBrushProperty, "hairlineBrush");
        if (head) cell.SetResourceReference(Border.BackgroundProperty, "surfaceBrush");
        return cell;
    }

    /// <summary>
    /// Bir satırın bir motordaki çevirisi: seçilebilir hücre. Seçili hücre sarı blok değil: hafif sarı zemin (%12), sarı yazı
    /// ve tik. Motor çeviremediyse ilk satırda Türkçe neden, diğerlerinde boş.
    /// </summary>
    private FrameworkElement Choice(int row, int column)
    {
        var engine = columns[column].Engine;
        var result = results.GetValueOrDefault(engine);
        if (result is { Failed: true })
        {
            var reason = row == 0 ? ScreenKit.Small(result.Error ?? "Bu motor şu an çeviremedi.", 13, "liveTextBrush") : ScreenKit.Small("", 13);
            return Cell(reason);
        }
        var translated = result?.Lines.ElementAtOrDefault(row);
        var text = running.Contains(engine) ? "çevriliyor…" : translated ?? "—";
        var picked = votes.Picked(row) == column;
        var content = new DockPanel();
        if (picked)
        {
            var check = new TextBlock { Text = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = Sp.T("xs", "xxs", "0", "0") };
            check.SetResourceReference(StyleProperty, "GlyphText");
            check.SetResourceReference(TextBlock.ForegroundProperty, "amberTextBrush");
            DockPanel.SetDock(check, Dock.Right);
            content.Children.Add(check);
        }
        var words = new TextBlock { Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap, LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            FontWeight = picked ? FontWeights.Medium : FontWeights.Normal };
        words.SetResourceReference(TextBlock.ForegroundProperty, picked ? "amberTextBrush" : translated is null ? "mutedBrush" : "textBrush");
        content.Children.Add(words);
        var button = new Button
        {
            Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Padding = Sp.T("m", "s", "m", "s"), Cursor = System.Windows.Input.Cursors.Hand, Template = ChoiceTemplate(), IsEnabled = translated is not null,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, BorderThickness = new Thickness(0, 0, 0, 1),
        };
        button.SetResourceReference(BorderBrushProperty, "hairlineBrush");
        if (picked)
        {
            button.SetResourceReference(BackgroundProperty, "amberSoftBrush");
            button.Tag = "picked";
        }
        else button.Background = Brushes.Transparent;
        button.SetResourceReference(FocusVisualStyleProperty, "SegmentFocusRing");
        AutomationProperties.SetName(button, $"{BlindTest.HiddenName(column)}: {text}{(picked ? ", seçili" : "")}");
        button.Click += (_, _) =>
        {
            votes.Pick(row, column);
            ShowCompare();
        };
        return button;
    }

    private static ControlTemplate ChoiceTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Cell");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
        presenter.SetValue(VerticalAlignmentProperty, new TemplateBindingExtension(VerticalContentAlignmentProperty));
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new MultiTrigger();
        hover.Conditions.Add(new System.Windows.Condition(IsMouseOverProperty, true));
        hover.Conditions.Add(new System.Windows.Condition(TagProperty, null));
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("fieldBrush"), "Cell"));
        template.Triggers.Add(hover);
        return template;
    }

    /// <summary>Kazanan: "Motor B önde · 3 seçim" ve "Bu motoru kullan" (seçili motorsa "Zaten bu motoru kullanıyorsun").</summary>
    private FrameworkElement Winner()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = Sp.T("0", "m", "0", "0") };
        if (votes.Winner is not { } winner || winner >= columns.Count)
        {
            if (votes.Count > 0) bar.Children.Add(ScreenKit.Muted("Berabere. Birkaç satır daha seç.", 14));
            return bar;
        }
        var column = columns[winner];
        var name = revealed ? BlindTest.EngineName(column.Engine) : BlindTest.HiddenName(winner);
        var label = ScreenKit.Text($"{name} önde · {votes.VotesText(winner)}", 15, FontWeights.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = Sp.T("0", "0", "m", "0");
        bar.Children.Add(label);
        if (controller.Settings.Engine == column.Engine)
        {
            var already = ScreenKit.Muted("Zaten bu motoru kullanıyorsun.", 14);
            already.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(already);
            return bar;
        }
        var use = ScreenKit.Button("Bu motoru kullan", "OutlineButton", (_, _) =>
        {
            controller.UseEngine(column.Engine);
            setupEngine = column.Engine;
            revealed = true;
            ShowEngines();
            ShowAdvanced();
            ShowCompare();
        });
        bar.Children.Add(use);
        return bar;
    }
}
