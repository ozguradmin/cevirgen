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
using CouchSubs.App.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>
/// Çeviri sekmesi (taslak: design/cevirgen/arayuz-taslagi/Translation.dc.html). Üstte "Kör test": beş zor replik en çok üç
/// motora (ücretsiz yerel model hep; anahtarı kayıtlı motorlar; yoksa "anahtar gerekli" sütunu) gerçekten çevrilir, motorların
/// adı "Motor A/B/C" olarak gizlidir ve sütun sırası karışıktır; her satırda bir çeviri seçilir, seçimler sayılır,
/// "Motorları göster" adları ve ücret/hız bilgisini açar, "Bu motoru kullan" kazananı uygular. Altta motor seçimi ve seçili
/// motorun üç adımlı kurulumu (anahtar sayfasını aç → anahtarı yapıştır, şifreli depoya → "Bağlantıyı dene", gecikmesiyle);
/// "İleri"de model, hız, oyun dili, yerel sunucu ve ek talimat. Anahtarsız da çalışır: ücretsiz yerel model.
/// </summary>
public sealed class TranslationPage : UserControl
{
    /// <summary>Ekran görüntüsü aracı için hazır durum (gerçek motor çağrılmaz).</summary>
    internal sealed record PreviewState(IReadOnlyList<BlindTestColumn> Columns, IReadOnlyDictionary<TranslationEngine, BlindTestResult> Results,
        IReadOnlyDictionary<int, int> Picks, bool Revealed, bool AdvancedOpen = false, string? TestResult = null, TranslationEngine? Setup = null);

    internal static PreviewState? Preview { get; set; }

    /// <summary>Bu çalışmadaki son test (sekme yeniden açılınca çeviriler yeniden istenmez).</summary>
    private static IReadOnlyList<BlindTestColumn>? lastColumns;
    private static readonly Dictionary<TranslationEngine, BlindTestResult> LastResults = [];

    private static readonly TranslationEngine[] Engines =
        [TranslationEngine.Free, TranslationEngine.Gemini, TranslationEngine.DeepL, TranslationEngine.OpenAi, TranslationEngine.Claude, TranslationEngine.Local];

    private readonly AppController controller;
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly Grid table = new();
    private readonly StackPanel winnerBar = new() { Orientation = Orientation.Horizontal };
    private readonly Button revealButton;
    private readonly Button runButton;
    private readonly UniformGrid engineGrid = new() { Columns = 3 };
    private readonly ContentControl setupHost = new() { Focusable = false };
    private readonly StackPanel advanced = new();
    private readonly Button advancedToggle;
    private readonly BlindTestVotes votes = new();
    private readonly Dictionary<TranslationEngine, BlindTestResult> results = [];
    private readonly HashSet<TranslationEngine> running = [];
    private IReadOnlyList<BlindTestColumn> columns;
    private bool revealed;
    /// <summary>Kurulumu gösterilen motor (seçili motor; "anahtar gerekli" bağlantısı da değiştirir).</summary>
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
        revealButton = ScreenKit.Button("Motorları göster", "SecondaryButton", (_, _) => { revealed = !revealed; ShowTable(); });
        revealButton.MinHeight = 40;
        runButton = ScreenKit.Button("Testi çalıştır", "OutlineButton", async (_, _) => await RunAsync(force: true));
        runButton.MinHeight = 40;
        advancedToggle = ScreenKit.Button("İleri ayarlar ▾", "TextLink", (_, _) => { advancedOpen = !advancedOpen; ShowAdvanced(); });

        if (Preview is { } preview)
        {
            columns = preview.Columns;
            foreach (var (engine, result) in preview.Results) results[engine] = result;
            foreach (var (row, column) in preview.Picks) votes.Pick(row, column);
            revealed = preview.Revealed;
            advancedOpen = preview.AdvancedOpen;
            testResult = preview.TestResult;
            testOk = true;
            setupEngine = preview.Setup ?? setupEngine;
        }
        else
        {
            // Sütunlar aynı motorlarsa son testin sırası ve çevirileri korunur.
            var wanted = controller.BlindTestColumns();
            columns = lastColumns is { } last && last.OrderBy(c => c.Engine).SequenceEqual(wanted.OrderBy(c => c.Engine))
                ? last : BlindTest.Shuffle(wanted, new Random());
            lastColumns = columns;
            foreach (var column in columns)
                if (LastResults.TryGetValue(column.Engine, out var cached)) results[column.Engine] = cached;
        }

        var stack = new StackPanel { Margin = Sp.T("edge", "l", "edge", "xl") };
        stack.Children.Add(BuildHeader());
        var tableFrame = new Border { BorderThickness = new Thickness(1), Child = table, Margin = Sp.T("0", "m", "0", "0") };
        tableFrame.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        AutomationProperties.SetName(tableFrame, "Örnek çeviriler");
        stack.Children.Add(tableFrame);
        var note = ScreenKit.Mono("Çeviriler bu bilgisayardan gerçek motorlarla istenir; süreler satır başına ortalamadır. Hücreye tıklayarak seç.", 12);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = Sp.T("0", "s", "0", "0");
        stack.Children.Add(note);
        winnerBar.Margin = Sp.T("0", "m", "0", "0");
        stack.Children.Add(winnerBar);

        stack.Children.Add(BuildEngineSection());
        stack.Children.Add(setupHost);
        advancedToggle.Margin = Sp.T("0", "l", "0", "0");
        advancedToggle.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(advancedToggle);
        advanced.Margin = Sp.T("0", "s", "0", "0");
        stack.Children.Add(advanced);
        scroll.Content = stack;
        Content = scroll;

        controller.PropertyChanged += OnChanged;
        Unloaded += (_, _) => controller.PropertyChanged -= OnChanged;
        Loaded += async (_, _) =>
        {
            controller.PropertyChanged -= OnChanged;
            controller.PropertyChanged += OnChanged;
            if (Preview is null) await RunAsync(force: false);
        };
        ShowTable();
        ShowEngines();
        ShowSetup();
        ShowAdvanced();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppController.Settings) or nameof(AppController.HasDeepLKey) or nameof(AppController.UsesFreeGemini))) return;
        Dispatcher.BeginInvoke(() =>
        {
            ShowEngines();
            ShowWinner();
            if (!IsKeyboardFocusWithin || keyBox?.IsKeyboardFocusWithin != true) ShowSetup();
        });
    }

    // ───── Kör test ─────

    private FrameworkElement BuildHeader()
    {
        var head = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        runButton.Margin = Sp.T("0", "0", "xs", "0");
        buttons.Children.Add(runButton);
        buttons.Children.Add(revealButton);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        var texts = new StackPanel { Margin = Sp.T("0", "0", "l", "0") };
        texts.Children.Add(ScreenKit.Title("Kör test"));
        var lead = ScreenKit.Muted("Beş zor repliği en çok üç motora çevirtip adlarını gizledik. Her satırda kulağına doğru gelen çeviriyi seç; sonra hangi motorun kazandığını gör.", 15);
        lead.MaxWidth = 760;
        lead.HorizontalAlignment = HorizontalAlignment.Left;
        lead.Margin = Sp.T("0", "titleBody", "0", "0");
        texts.Children.Add(lead);
        head.Children.Add(texts);
        return head;
    }

    /// <summary>Kullanılabilen motorların çevirileri istenir (sırayla değil, aynı anda); <paramref name="force"/> yoksa yalnız eksikler.</summary>
    private async Task RunAsync(bool force)
    {
        if (Preview is not null) return;
        if (force)
        {
            foreach (var column in columns) LastResults.Remove(column.Engine);
            results.Clear();
            votes.Clear();
        }
        var pending = columns.Where(column => column.Ready && !results.ContainsKey(column.Engine) && !running.Contains(column.Engine)).ToList();
        if (pending.Count == 0)
        {
            ShowTable();
            return;
        }
        foreach (var column in pending) running.Add(column.Engine);
        ShowTable();
        await Task.WhenAll(pending.Select(async column =>
        {
            BlindTestResult result;
            try { result = await controller.TranslateSamplesAsync(column.Engine, BlindTest.Lines); }
            catch (Exception error) { result = new BlindTestResult([.. BlindTest.Lines.Select(_ => (string?)null)], null, error.Message); }
            results[column.Engine] = result;
            LastResults[column.Engine] = result;
            running.Remove(column.Engine);
            ShowTable();
        }));
    }

    /// <summary>Tablo: Replik sütunu ve motor sütunları; başlıkta ad (gizliyse Motor A/B/C) ve seçim sayısı, en altta ücret · hız.</summary>
    private void ShowTable()
    {
        table.Children.Clear();
        table.ColumnDefinitions.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 200 });
        foreach (var _ in columns) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < BlindTest.Lines.Count + 2; r++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Başlık satırı.
        Place(HeadCell(ScreenKit.Label("Replik"), shaded: true), 0, 0);
        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            var name = ScreenKit.Text(revealed ? BlindTest.EngineName(column.Engine) : BlindTest.HiddenName(c), 14, FontWeights.SemiBold, wrap: false);
            var count = ScreenKit.Mono(votes.VotesText(c), 13, "amberTextBrush");
            var line = new DockPanel();
            DockPanel.SetDock(count, Dock.Right);
            count.Margin = Sp.T("xs", "0", "0", "0");
            line.Children.Add(count);
            line.Children.Add(name);
            Place(HeadCell(line, shaded: true, divider: true), 0, c + 1);
        }

        for (var r = 0; r < BlindTest.Lines.Count; r++)
        {
            var english = ScreenKit.Muted(BlindTest.Lines[r], 14);
            english.LineHeight = 20;
            Place(Cell(english, top: true), r + 1, 0);
            for (var c = 0; c < columns.Count; c++) Place(Choice(r, c), r + 1, c + 1);
        }

        var last = BlindTest.Lines.Count + 1;
        Place(HeadCell(ScreenKit.Label("Ücret · hız"), shaded: true, top: true), last, 0);
        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            var result = results.GetValueOrDefault(column.Engine);
            var facts = !column.Ready ? "anahtar gerekli"
                : result?.Error is { } error && result.Failed ? "hata: " + error
                : revealed ? BlindTest.FactsWithSpeed(column.Engine, result?.AverageMs) : "gizli";
            var text = ScreenKit.Mono(facts, 12);
            text.TextWrapping = TextWrapping.Wrap;
            text.LineHeight = 18;
            Place(HeadCell(text, shaded: true, top: true, divider: true), last, c + 1);
        }
        revealButton.Content = revealed ? "Motorları gizle" : "Motorları göster";
        runButton.Content = running.Count > 0 ? "Çevriliyor…" : results.Count > 0 ? "Yeniden çevir" : "Testi çalıştır";
        runButton.IsEnabled = running.Count == 0;
        ShowWinner();
    }

    private void Place(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        table.Children.Add(element);
    }

    private static Border HeadCell(UIElement child, bool shaded, bool divider = false, bool top = false)
    {
        var cell = new Border { Child = child, Padding = Sp.T("m", "s", "m", "s"), BorderThickness = new Thickness(divider ? 1 : 0, top ? 1 : 0, 0, 0) };
        cell.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        if (shaded) cell.SetResourceReference(Border.BackgroundProperty, "surfaceBrush");
        return cell;
    }

    private static Border Cell(UIElement child, bool top) => Pad(HeadCell(child, shaded: false, top: top));

    private static Border Pad(Border cell)
    {
        cell.Padding = Sp.T("m", "item", "m", "item");
        return cell;
    }

    /// <summary>Bir satırın bir motordaki çevirisi: seçilebilir hücre (seçiliyse sarı). Motor hazır değilse anahtar bağlantısı.</summary>
    private FrameworkElement Choice(int row, int column)
    {
        var engine = columns[column].Engine;
        if (!columns[column].Ready)
        {
            var empty = new StackPanel();
            if (row == 0)
            {
                empty.Children.Add(ScreenKit.Muted("anahtar gerekli", 14));
                var link = ScreenKit.Button($"{BlindTest.EngineName(engine)} anahtarını ekle → 1. adım", "TextLink", (_, _) => GoToSetup(engine));
                link.HorizontalAlignment = HorizontalAlignment.Left;
                link.Margin = Sp.T("0", "xxs", "0", "0");
                empty.Children.Add(link);
            }
            var cell = Pad(HeadCell(empty, shaded: false, divider: true, top: true));
            return cell;
        }
        var result = results.GetValueOrDefault(engine);
        var text = running.Contains(engine) ? "çevriliyor…" : result is null ? "—" : result.Lines.ElementAtOrDefault(row) ?? "çevrilemedi";
        var picked = votes.Picked(row) == column;
        var content = new TextBlock { Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap, LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            FontWeight = picked ? FontWeights.SemiBold : FontWeights.Normal };
        content.SetResourceReference(TextBlock.ForegroundProperty, picked ? "onAmberBrush" : result?.Lines.ElementAtOrDefault(row) is null ? "mutedBrush" : "textBrush");
        var button = new Button
        {
            Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top,
            Padding = Sp.T("m", "item", "m", "item"), BorderThickness = new Thickness(1, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
            Template = CellTemplate(), IsEnabled = result?.Lines.ElementAtOrDefault(row) is not null,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 0,
        };
        button.SetResourceReference(BorderBrushProperty, "borderBrush");
        // Seçili hücre sarı kalır (üzerine gelince alan rengine dönmez; şablondaki tetik Tag'e bakar).
        if (picked)
        {
            button.SetResourceReference(BackgroundProperty, "amberBrush");
            button.Tag = "picked";
        }
        else button.Background = Brushes.Transparent;
        button.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
        AutomationProperties.SetName(button, $"{BlindTest.HiddenName(column)}: {text}{(picked ? ", seçili" : "")}");
        button.Click += (_, _) =>
        {
            votes.Pick(row, column);
            ShowTable();
        };
        return button;
    }

    private static ControlTemplate CellTemplate()
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
    private void ShowWinner()
    {
        winnerBar.Children.Clear();
        if (votes.Winner is not { } winner || winner >= columns.Count)
        {
            if (votes.Count > 0)
                winnerBar.Children.Add(ScreenKit.Muted("Berabere. Birkaç satır daha seç.", 14));
            return;
        }
        var column = columns[winner];
        var name = revealed ? BlindTest.EngineName(column.Engine) : BlindTest.HiddenName(winner);
        var label = ScreenKit.Text($"{name} önde · {votes.VotesText(winner)}", 15, FontWeights.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = Sp.T("0", "0", "m", "0");
        winnerBar.Children.Add(label);
        if (controller.Settings.Engine == column.Engine)
        {
            winnerBar.Children.Add(ScreenKit.Muted("Zaten bu motoru kullanıyorsun.", 14));
            return;
        }
        var use = ScreenKit.Button("Bu motoru kullan", "PrimaryButton", (_, _) =>
        {
            controller.UseEngine(column.Engine);
            setupEngine = column.Engine;
            revealed = true;
            ShowTable();
            ShowEngines();
            ShowSetup();
        });
        use.MinHeight = 40;
        winnerBar.Children.Add(use);
    }

    // ───── Motor seçimi ve kurulum ─────

    private FrameworkElement BuildEngineSection()
    {
        var section = new StackPanel { Margin = Sp.T("0", "xl", "0", "0") };
        section.Children.Add(ScreenKit.Title("Çeviri motoru", 24));
        var lead = ScreenKit.Muted("Anahtarsız da çalışır: ücretsiz yerel model bu bilgisayarda çevirir, yazılar dışarı çıkmaz. Daha doğal çeviri için kendi anahtarınla bir motor seç; anahtar yalnız bu bilgisayarda, şifreli saklanır.", 15);
        lead.MaxWidth = 820;
        lead.HorizontalAlignment = HorizontalAlignment.Left;
        lead.Margin = Sp.T("0", "titleBody", "0", "m");
        section.Children.Add(lead);
        AutomationProperties.SetName(engineGrid, "Çeviri motorları");
        // Kartların sağ payı ızgaranın dışına taşar: kenarlar tablo ve kurulum kutusuyla hizalı.
        engineGrid.Margin = Sp.T("0", "0", "-xs", "0");
        section.Children.Add(engineGrid);
        return section;
    }

    /// <summary>Altı motor: ad, ücret, durum (kullanılıyor / anahtar kayıtlı / anahtar gerekli); seçilen motorun kurulumu altta.</summary>
    private void ShowEngines()
    {
        engineGrid.Children.Clear();
        var current = controller.Settings.Engine;
        foreach (var engine in Engines)
        {
            var active = engine == current;
            var shown = engine == setupEngine;
            var ready = engine == TranslationEngine.Free || controller.IsReady(engine);
            // Yerel sunucu adres geçerliyse "ayarlı" (çalışıp çalışmadığı 3. adımda denenir).
            var status = active ? "kullanılıyor" : engine == TranslationEngine.Free ? "anahtarsız"
                : ready ? engine == TranslationEngine.Local ? "ayarlı" : "hazır" : "anahtar gerekli";
            var texts = new StackPanel();
            var top = new DockPanel();
            var state = ScreenKit.Mono(status, 12, active ? "amberTextBrush" : ready ? "successBrush" : "mutedBrush");
            DockPanel.SetDock(state, Dock.Right);
            state.Margin = Sp.T("xs", "0", "0", "0");
            top.Children.Add(state);
            top.Children.Add(ScreenKit.Text(BlindTest.EngineName(engine), 15, FontWeights.SemiBold, wrap: false));
            texts.Children.Add(top);
            var facts = ScreenKit.Muted(BlindTest.Facts(engine), 13);
            facts.Margin = Sp.T("0", "xxs", "0", "0");
            texts.Children.Add(facts);
            var button = new Button
            {
                Content = texts, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Padding = Sp.T("m", "s", "m", "s"), Margin = Sp.T("0", "0", "xs", "xs"), BorderThickness = new Thickness(shown ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.Hand, Template = CellTemplate(), MinHeight = 64,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            };
            button.SetResourceReference(BorderBrushProperty, shown ? "amberBrush" : "borderBrush");
            if (shown) button.SetResourceReference(BackgroundProperty, "surfaceBrush");
            else button.Background = Brushes.Transparent;
            if (shown) button.Tag = "shown";
            button.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
            AutomationProperties.SetName(button, $"{BlindTest.EngineName(engine)}: {status}");
            var chosen = engine;
            button.Click += (_, _) =>
            {
                setupEngine = chosen;
                testResult = null;
                // Hazır motor hemen kullanılır; anahtarı olmayan motorda önce kurulum (anahtar kaydedilince seçilir).
                if (chosen == TranslationEngine.Free || controller.IsReady(chosen)) controller.UseEngine(chosen);
                ShowEngines();
                ShowSetup();
                ShowAdvanced();
            };
            engineGrid.Children.Add(button);
        }
    }

    private void GoToSetup(TranslationEngine engine)
    {
        setupEngine = engine;
        testResult = null;
        ShowEngines();
        ShowSetup();
        ShowAdvanced();
        Dispatcher.BeginInvoke(() => setupAnchor?.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Seçilen motorun kurulumu: ücretsiz modelde bilgi; bulut motorlarında ve yerel sunucuda üç adım.</summary>
    private void ShowSetup()
    {
        var engine = setupEngine;
        var frame = new Border { BorderThickness = new Thickness(1), Margin = Sp.T("0", "xs", "0", "0") };
        frame.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        setupAnchor = frame;
        AutomationProperties.SetName(frame, $"{BlindTest.EngineName(engine)} kurulumu");
        if (engine == TranslationEngine.Free)
        {
            var info = new StackPanel { Margin = Sp.T("l", "m", "l", "m") };
            info.Children.Add(ScreenKit.Text("Anahtarsız da çalışır: ücretsiz yerel model", 15, FontWeights.SemiBold));
            var body = ScreenKit.Muted(controller.ModelInstalled
                ? "Model bu bilgisayarda kurulu. Çeviri çevrimdışı yapılır, altyazı yazıları hiçbir yere gönderilmez."
                : "Model henüz inmedi; çeviri ilk başlatılışta indirilir. Çeviri çevrimdışı yapılır.", 14);
            body.Margin = Sp.T("0", "xxs", "0", "0");
            info.Children.Add(body);
            frame.Child = info;
            setupHost.Content = frame;
            return;
        }
        var grid = new Grid();
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Step(0, "Adım 1", Step1(engine)));
        grid.Children.Add(Step(1, "Adım 2", Step2(engine)));
        grid.Children.Add(Step(2, "Adım 3", Step3(engine)));
        frame.Child = grid;
        setupHost.Content = frame;
    }

    private static Border Step(int index, string label, IEnumerable<UIElement> items)
    {
        var stack = new StackPanel();
        var title = ScreenKit.Label(label);
        title.Margin = Sp.T("0", "0", "0", "xs");
        stack.Children.Add(title);
        foreach (var item in items) stack.Children.Add(item);
        var cell = new Border { Child = stack, Padding = Sp.T("l", "m", "l", "m"), BorderThickness = new Thickness(index == 0 ? 0 : 1, 0, 0, 0) };
        cell.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        Grid.SetColumn(cell, index);
        return cell;
    }

    private IEnumerable<UIElement> Step1(TranslationEngine engine)
    {
        var (title, link, how) = engine switch
        {
            TranslationEngine.Gemini => ("Ücretsiz anahtarı al", "Google AI Studio'yu aç ↗", "Google hesabınla gir, “Create API key”e bas."),
            TranslationEngine.DeepL => ("Ücretsiz anahtarı al", "DeepL API sayfasını aç ↗", "Ücretsiz DeepL API hesabı aç, hesap sayfasından anahtarı kopyala."),
            TranslationEngine.OpenAi => ("Anahtarı al", "OpenAI anahtar sayfasını aç ↗", "Hesabınla gir, “Create new secret key”e bas. Kullanım ücretlidir."),
            TranslationEngine.Claude => ("Anahtarı al", "Anthropic Console'u aç ↗", "Hesabınla gir, “Create Key”e bas. Kullanım ücretlidir."),
            _ => ("Yerel sunucuyu kur", "Ollama'yı indir ↗", "Ollama'yı kur ve bir model indir: ollama pull qwen2.5:7b. LM Studio da olur."),
        };
        yield return ScreenKit.Text(title, 15, FontWeights.SemiBold);
        var uri = engine.Provider()?.KeyPage() ?? new Uri("https://www.deepl.com/pro-api");
        var open = ScreenKit.Button(link, "TextLink", (_, _) => controller.OpenInBrowser(uri));
        open.SetResourceReference(ForegroundProperty, "amberTextBrush");
        open.FontSize = 14;
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = Sp.T("0", "xxs", "0", "xxs");
        yield return open;
        var hint = ScreenKit.Muted(how, 13);
        hint.LineHeight = 19;
        yield return hint;
    }

    private IEnumerable<UIElement> Step2(TranslationEngine engine)
    {
        var settings = controller.Settings;
        var saved = controller.HasKey(engine);
        if (engine == TranslationEngine.Local)
        {
            yield return ScreenKit.Text("Sunucu adresi ve model", 15, FontWeights.SemiBold);
            var address = new System.Windows.Controls.TextBox { Text = settings.LocalAiBaseUrl, Margin = Sp.T("0", "xs", "0", "0") };
            var model = new System.Windows.Controls.TextBox { Text = settings.LocalAiModel, Margin = Sp.T("0", "xs", "0", "0") };
            AutomationProperties.SetName(address, "Sunucu adresi");
            AutomationProperties.SetName(model, "Model adı");
            void Commit()
            {
                var (a, m) = (address.Text.Trim(), model.Text.Trim());
                if (a != controller.Settings.LocalAiBaseUrl || m != controller.Settings.LocalAiModel)
                    controller.Settings = controller.Settings with { LocalAiBaseUrl = a, LocalAiModel = m };
            }
            address.LostFocus += (_, _) => Commit();
            model.LostFocus += (_, _) => Commit();
            yield return ScreenKit.Field(address, LocalAiServer.OllamaUrl);
            yield return ScreenKit.Field(model, LocalAiServer.DefaultModel);
            var presets = new WrapPanel { Margin = Sp.T("0", "xs", "0", "0") };
            presets.Children.Add(ScreenKit.Button("Ollama", "CompactButton", (_, _) => { address.Text = LocalAiServer.OllamaUrl; Commit(); }));
            var lm = ScreenKit.Button("LM Studio", "CompactButton", (_, _) => { address.Text = LocalAiServer.LmStudioUrl; Commit(); });
            lm.Margin = Sp.T("xs", "0", "0", "0");
            presets.Children.Add(lm);
            yield return presets;
            var note = ScreenKit.Muted(saved ? "Anahtar kayıtlı (isteğe bağlı)." : "Anahtar gerekmez; sunucu istiyorsa 3. adımda yaz.", 13);
            note.Margin = Sp.T("0", "xs", "0", "0");
            yield return note;
            keyBox = new PasswordBox { Margin = Sp.T("0", "xs", "0", "0") };
            AutomationProperties.SetName(keyBox, "Sunucu anahtarı (isteğe bağlı)");
            yield return keyBox;
            yield break;
        }
        yield return ScreenKit.Text("Anahtarı yapıştır", 15, FontWeights.SemiBold);
        if (saved)
        {
            var row = new DockPanel { Margin = Sp.T("0", "xs", "0", "0") };
            var remove = ScreenKit.Button("Kaldır", "CompactButton", (_, _) =>
            {
                controller.RemoveKey(engine);
                testResult = null;
                ShowEngines();
                ShowSetup();
            }, $"{BlindTest.EngineName(engine)} anahtarını kaldır");
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var ok = ScreenKit.Mono("anahtar kayıtlı ●●●●●●", 13, "successBrush");
            ok.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(ok);
            yield return row;
        }
        keyBox = new PasswordBox { Margin = Sp.T("0", "xs", "0", "0"), MinHeight = 40 };
        keyBox.SetResourceReference(FontFamilyProperty, "MonoFont");
        AutomationProperties.SetName(keyBox, $"{BlindTest.EngineName(engine)} anahtarı");
        yield return keyBox;
        var stored = ScreenKit.Muted(saved ? "Yeni anahtar yazarsan eskisinin yerine geçer. Yalnız bu bilgisayarda, şifreli saklanır." : "Yalnız bu bilgisayarda, şifreli saklanır.", 13);
        stored.Margin = Sp.T("0", "xs", "0", "0");
        yield return stored;
    }

    private IEnumerable<UIElement> Step3(TranslationEngine engine)
    {
        yield return ScreenKit.Text("Dene", 15, FontWeights.SemiBold);
        var test = ScreenKit.Button("Bağlantıyı dene", "PrimaryButton", async (sender, _) => await TestAsync((Button)sender, engine));
        test.MinHeight = 40;
        test.Margin = Sp.T("0", "xs", "0", "xs");
        yield return test;
        if (testResult is { } result)
        {
            var line = ScreenKit.Mono(result, 13, testOk ? "successBrush" : "liveTextBrush");
            line.TextWrapping = TextWrapping.Wrap;
            line.LineHeight = 19;
            AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
            yield return line;
        }
        else
        {
            yield return ScreenKit.Muted(engine == TranslationEngine.Local ? "Sunucuya örnek bir satır gönderilir." : "Örnek bir satır çevrilir; anahtar kabul edilirse kaydedilir ve motor seçilir.", 13);
        }
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
            if (engine == TranslationEngine.Local)
            {
                var result = await controller.TestLocalAiAsync(typed);
                Report(result.State == OnlineKeyState.Valid, result.State.Describe(engine, text, controller.Settings.LocalAiServer), watch.ElapsedMilliseconds);
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
            ShowSetup();
        }
    }

    private void Report(bool ok, string message, long? ms)
    {
        testOk = ok;
        testResult = ok && ms is { } elapsed ? string.Create(CultureInfo.InvariantCulture, $"Çalışıyor · {elapsed} ms") : message;
        // Yeni motor çalışıyorsa kör testteki sütunu da açılır.
        if (ok && Preview is null)
        {
            var wanted = controller.BlindTestColumns();
            if (!wanted.OrderBy(c => c.Engine).SequenceEqual(columns.OrderBy(c => c.Engine)))
            {
                columns = BlindTest.Shuffle(wanted, new Random());
                lastColumns = columns;
                votes.Clear();
                _ = RunAsync(force: false);
            }
        }
    }

    // ───── İleri ─────

    /// <summary>İleri: model (yapay zekâ), ücretli Gemini anahtarı, çeviri hızı, oyun dili, ek talimat.</summary>
    private void ShowAdvanced()
    {
        advancedToggle.Content = advancedOpen ? "İleri ayarlar ▴" : "İleri ayarlar ▾";
        advanced.Children.Clear();
        advanced.Visibility = advancedOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!advancedOpen) return;
        var settings = controller.Settings;
        var engine = setupEngine;
        var provider = engine.Provider();

        if (provider is { } ai && ai != AiProvider.Local)
        {
            var models = new ComboBox { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Right };
            var selected = ai.Model(settings.ModelFor(ai)).Id;
            foreach (var option in ai.Models())
                models.Items.Add(new ComboBoxItem { Content = Localizer.Current[option.Label], Tag = option.Id, IsSelected = option.Id == selected });
            models.SelectionChanged += (_, _) =>
            {
                if (models.SelectedItem is ComboBoxItem { Tag: string id }) controller.Settings = controller.Settings.WithModel(ai, id);
            };
            AutomationProperties.SetName(models, "Model");
            advanced.Children.Add(Line("Model", models));
        }
        if (engine == TranslationEngine.Gemini)
        {
            var paid = new SwitchRow
            {
                Content = "Ücretli Gemini anahtarı kullanıyorum",
                Description = "Ücretsiz anahtarda Google yazıları ürünlerini geliştirmek için kullanabilir ve dakikalık sınır vardır; sınıra gelinen satırı ücretsiz model çevirir.",
                IsChecked = settings.GeminiPaidTier, Divider = false,
            };
            paid.Checked += (_, _) => controller.Settings = controller.Settings with { GeminiPaidTier = true };
            paid.Unchecked += (_, _) => controller.Settings = controller.Settings with { GeminiPaidTier = false };
            advanced.Children.Add(paid);
        }

        var pace = TranslationPaces.Match(settings);
        var paces = TranslationPaces.All;
        var paceIndex = pace is null ? -1 : paces.ToList().IndexOf(pace);
        var paceHint = ScreenKit.Muted(pace?.Hint ?? "Bekleme süreleri elle ayarlandı (Ayarlar › Gelişmiş).", 13);
        var paceBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        paceBox.Children.Add(ScreenKit.Segmented("Çeviri hızı", [.. paces.Select(p => p.Name)], paceIndex,
            index => controller.Settings = TranslationPaces.Apply(controller.Settings, paces[index]), minWidth: 88));
        advanced.Children.Add(Line("Çeviri hızı", paceBox));
        paceHint.Margin = Sp.T("0", "0", "0", "s");
        advanced.Children.Add(paceHint);

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
                advanced.Children.Add(ScreenKit.Muted("Çevrimiçi motorlar yalnız İngilizce oyunları çevirir; diğer dillerde ücretsiz model çevirir.", 13));
        }
        else
        {
            advanced.Children.Add(Line("Çeviri dili", ScreenKit.Mono("İngilizce → Türkçe", 14, "textBrush")));
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
            var hint = ScreenKit.Muted("Yapay zekâ çevirmeninin talimatının sonuna eklenir (en çok 1000 karakter). Sözlükteki kurallar ayrıca eklenir.", 13);
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
}
