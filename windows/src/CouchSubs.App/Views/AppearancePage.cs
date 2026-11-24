// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CouchSubs.App.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>
/// Görünüm sekmesi (taslak: design/cevirgen/arayuz-taslagi/Appearance.dc.html). Solda "Okunurluk testi": aynı Türkçe ve
/// İngilizce satır, kullanıcının gerçek altyazı görünümüyle (yazı boyu, kalınlık, kenarlık, zemin koyuluğu ve rengi, yazı
/// rengi, yer) dört zeminde (gece, gölge, gündüz gök, kar); her birinin altında hesaplanan sonuç (Okunur / Zor / Okunmaz;
/// <see cref="SubtitleLegibility"/>). Sağda dört hazır görünüm (<see cref="AppearancePresets"/>) ve ince ayar: yazı boyu,
/// kenarlık kalınlığı, zemin koyuluğu (− / +), konum (Üstünde / Örtsün / Sabit; Sabit'te yüzde), yazı rengi, satır sınırı
/// bilgisi; altta kalın yazı, tam genişlik bant, önceki satırlar ve konuşan adı. Her değişiklik hemen kaydedilir ve TV'ye
/// style mesajıyla gider.
/// </summary>
public sealed class AppearancePage : UserControl
{
    /// <summary>Örnek satırlar (taslaktaki gibi).</summary>
    private const string Turkish = "Gardiyanla birlikte git.";
    private const string English = "Go with the prison guard.";

    /// <summary>Yazı rengi seçenekleri: açık renkler (koyu yazı zeminsiz oyunda okunmaz).</summary>
    private static readonly (string Name, string Hex)[] TextColors =
        [("Beyaz", "#FFFFFF"), ("Sarı", "#FFE135"), ("Krem", "#F3EEE4"), ("Açık mavi", "#BFE3FF")];

    private readonly AppController controller;
    private readonly Grid tiles = new();
    private readonly List<(Grid Stage, TextBlock Verdict, LegibilityGround Ground)> scenes = [];
    private readonly StackPanel presetList = new();
    private readonly Stepper fontStepper;
    private readonly Stepper outlineStepper;
    private readonly Stepper plateStepper;
    private readonly ContentControl placementHost = new() { Focusable = false };
    private readonly StackPanel fixedRow = new();
    private readonly Slider fixedSlider = new() { Minimum = 0, Maximum = 85, SmallChange = 1, LargeChange = 5, IsMoveToPointEnabled = true };
    private readonly TextBlock fixedValue = ScreenKit.Mono("", 14, "textBrush");
    private readonly WrapPanel swatches = new();
    private readonly SwitchRow bold = new() { Content = "Kalın yazı" };
    private readonly SwitchRow band = new() { Content = "Tam genişlikte zemin", Description = "Zemin kutusu ekranın iki yanına uzanır; yan yana taşan İngilizceyi de örter." };
    private readonly SwitchRow previous = new() { Content = "Hızlı konuşmada önceki satırlar üstte", Description = "Kapalıysa yeni satır eskisinin yerine geçer." };
    private readonly SwitchRow names = new() { Content = "Konuşan adını göster", Description = "Satırın başındaki karakter adı (JASON: …) kalır; ad çevrilmez." };
    private readonly SwitchRow nameColors = new() { Content = "Konuşan adlarını renklendir", Description = "Yalnız ad kendi renginde; satır yazı renginde kalır.", Divider = false };
    private bool loading;

    public AppearancePage(AppController controller)
    {
        this.controller = controller;
        DataContext = controller;
        controller.NoteScreen("appearance");
        fontStepper = new Stepper("Küçült", "Büyüt", () => Change(style => AppearanceSteps.FontSize(style, -1)), () => Change(style => AppearanceSteps.FontSize(style, +1)));
        outlineStepper = new Stepper("İncelt", "Kalınlaştır", () => Change(style => AppearanceSteps.Outline(style, -1)), () => Change(style => AppearanceSteps.Outline(style, +1)));
        plateStepper = new Stepper("Azalt", "Artır", () => Change(style => AppearanceSteps.Background(style, -1)), () => Change(style => AppearanceSteps.Background(style, +1)));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 520 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        var left = BuildTest();
        grid.Children.Add(left);
        var rule = ScreenKit.Rule(vertical: true);
        Grid.SetColumn(rule, 1);
        grid.Children.Add(rule);
        var aside = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            Content = BuildControls(),
        };
        Grid.SetColumn(aside, 2);
        grid.Children.Add(aside);
        Content = grid;

        controller.PropertyChanged += OnChanged;
        Unloaded += (_, _) => controller.PropertyChanged -= OnChanged;
        Loaded += (_, _) =>
        {
            controller.PropertyChanged -= OnChanged;
            controller.PropertyChanged += OnChanged;
            Show();
        };
        Show();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppController.Settings)) Dispatcher.BeginInvoke(Show);
    }

    private SubtitleStyle CurrentStyle => controller.Settings.Style;

    /// <summary>Biçim değişir, kaydedilir ve TV'ye gider (denetleyici style mesajını kendisi yollar).</summary>
    private void Change(Func<SubtitleStyle, SubtitleStyle> change)
    {
        if (loading) return;
        var settings = controller.Settings;
        controller.Settings = settings with { Style = change(settings.Style) };
    }

    // ───── Sol: okunurluk testi ─────

    private FrameworkElement BuildTest()
    {
        var stack = new StackPanel { Margin = Sp.T("edge", "l", "edge", "l") };
        var head = new WrapPanel { Margin = Sp.T("0", "0", "0", "m") };
        var title = ScreenKit.Title("Okunurluk testi");
        title.Margin = Sp.T("0", "0", "m", "0");
        head.Children.Add(title);
        var subtitle = ScreenKit.Muted("Aynı satır, oyunun en karanlık ve en aydınlık anlarında. Değişiklikler TV'ye anında gider.");
        subtitle.VerticalAlignment = VerticalAlignment.Bottom;
        subtitle.Margin = Sp.T("0", "0", "0", "xxs");
        head.Children.Add(subtitle);
        stack.Children.Add(head);

        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Sp.V("s")) });
        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tiles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Sp.V("m")) });
        tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < SubtitleLegibility.Grounds.Count; i++)
        {
            var ground = SubtitleLegibility.Grounds[i];
            var stage = new Grid { ClipToBounds = true, Background = new SolidColorBrush(SpeakerText.Rgb(ground.Hex)) };
            stage.SizeChanged += (_, _) => DrawScene(stage);
            var frame = new Border { BorderThickness = new Thickness(1), Child = new AspectBox { Child = stage } };
            frame.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
            var verdict = ScreenKit.Small("", 13);
            var caption = new DockPanel { Margin = Sp.T("0", "xs", "0", "0") };
            DockPanel.SetDock(verdict, Dock.Right);
            caption.Children.Add(verdict);
            caption.Children.Add(ScreenKit.Label(ground.Label));
            var figure = new StackPanel();
            figure.Children.Add(frame);
            figure.Children.Add(caption);
            AutomationProperties.SetName(figure, ground.Name);
            Grid.SetColumn(figure, i % 2 * 2);
            Grid.SetRow(figure, i / 2 * 2);
            tiles.Children.Add(figure);
            scenes.Add((stage, verdict, ground));
        }
        stack.Children.Add(tiles);
        var note = ScreenKit.Muted("Karolar TV ekranının alt ortasını gösterir; yazı boyu, kenarlık ve oyunun İngilizcesi TV'deki oranında.", 13);
        note.Margin = Sp.T("0", "m", "0", "0");
        stack.Children.Add(note);
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = stack,
        };
    }

    /// <summary>
    /// Karonun sahnesi: altta İngilizce satır (oyunun beyaz, hafif gölgeli altyazısı) ve yerleşime göre üstünde / üstüne /
    /// altında Türkçe satır. Karo TV'nin alt ortasındaki 960 px'i gösterir; ölçek karo genişliği / 960.
    /// </summary>
    private void DrawScene(Grid stage)
    {
        stage.Children.Clear();
        var width = stage.ActualWidth;
        if (width <= 0) return;
        var style = CurrentStyle;
        var scale = width / SubtitleLegibility.TileTvWidth;
        var english = new OutlinedText
        {
            Text = English, FontSize = SubtitleLegibility.EnglishPx * scale, FontWeight = FontWeights.SemiBold, Fill = Brushes.White,
            Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = Math.Max(0.5, scale), BlurRadius = 2 * scale, Opacity = 0.9 },
        };
        english.SetResourceReference(OutlinedText.FontFamilyProperty, "TextFont");
        var turkish = SubtitleLine.Make(Turkish, style, scale);
        var block = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(width * 0.06, 0, width * 0.06, stage.ActualHeight * 0.1) };
        var gap = 8 * scale;
        switch (style.Placement)
        {
            case SubtitlePlacement.Cover:
                english.HorizontalAlignment = HorizontalAlignment.Center;
                english.VerticalAlignment = VerticalAlignment.Center;
                turkish.VerticalAlignment = VerticalAlignment.Center;
                block.Children.Add(english);
                block.Children.Add(turkish);
                break;
            case SubtitlePlacement.Below:
                block.Children.Add(Column(english, turkish, gap));
                break;
            default:
                block.Children.Add(Column(turkish, english, gap));
                break;
        }
        stage.Children.Add(block);
    }

    private static StackPanel Column(FrameworkElement top, FrameworkElement bottom, double gap)
    {
        top.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Margin = new Thickness(0, gap, 0, 0);
        var column = new StackPanel();
        column.Children.Add(top);
        column.Children.Add(bottom);
        return column;
    }

    // ───── Sağ: hazır görünümler ve ince ayar ─────

    private FrameworkElement BuildControls()
    {
        var stack = new StackPanel { Margin = Sp.T("edge", "l", "edge", "l") };
        var presetsTitle = ScreenKit.Label("Hazır görünüm");
        presetsTitle.Margin = Sp.T("0", "0", "0", "s");
        stack.Children.Add(presetsTitle);
        AutomationProperties.SetName(presetList, "Hazır görünüm");
        stack.Children.Add(presetList);

        var fineTitle = ScreenKit.Label("İnce ayar");
        fineTitle.Margin = Sp.T("0", "l", "0", "xs");
        stack.Children.Add(fineTitle);
        stack.Children.Add(StepRow("Yazı boyu", fontStepper));
        stack.Children.Add(StepRow("Kenarlık kalınlığı", outlineStepper));
        stack.Children.Add(StepRow("Zemin koyuluğu", plateStepper));
        stack.Children.Add(StepRow("Konum", placementHost));

        // Sabit konumda ekrandaki yer (alttan yüzde); sürüklerken TV'de örnek altyazı görünür.
        var fixedHead = new DockPanel();
        DockPanel.SetDock(fixedValue, Dock.Right);
        fixedHead.Children.Add(fixedValue);
        fixedHead.Children.Add(ScreenKit.Text("Ekrandaki yer (alttan)"));
        fixedRow.Children.Add(fixedHead);
        fixedSlider.Margin = Sp.T("0", "xs", "0", "0");
        AutomationProperties.SetName(fixedSlider, "Ekrandaki yer, alttan yüzde");
        fixedSlider.ValueChanged += (_, e) =>
        {
            fixedValue.Text = string.Create(CultureInfo.InvariantCulture, $"%{Math.Round(e.NewValue):0}");
            Change(style => style with { BottomPercent = (int)Math.Round(e.NewValue) });
        };
        fixedSlider.PreviewMouseDown += (_, _) => controller.SendTestSubtitle();
        fixedRow.Children.Add(fixedSlider);
        stack.Children.Add(ScreenKit.Row(fixedRow));

        var colorRow = new DockPanel();
        DockPanel.SetDock(swatches, Dock.Right);
        swatches.HorizontalAlignment = HorizontalAlignment.Right;
        colorRow.Children.Add(swatches);
        var colorName = ScreenKit.Text("Yazı rengi");
        colorName.VerticalAlignment = VerticalAlignment.Center;
        colorRow.Children.Add(colorName);
        stack.Children.Add(ScreenKit.Row(colorRow));

        var limit = new DockPanel();
        var limitValue = ScreenKit.Mono(AppearanceSteps.LineLimit, 14);
        DockPanel.SetDock(limitValue, Dock.Right);
        limit.Children.Add(limitValue);
        limit.Children.Add(ScreenKit.Text("Satır sınırı"));
        stack.Children.Add(ScreenKit.Row(limit));
        var limitNote = ScreenKit.Muted("Satır sınırı profesyonel altyazı kuralıdır: uzun çeviriler iki dengeli satıra bölünür, ekranın üçte birini kaplamaz.", 13);
        limitNote.Margin = Sp.T("0", "xs", "0", "0");
        stack.Children.Add(limitNote);

        var moreTitle = ScreenKit.Label("Diğer");
        moreTitle.Margin = Sp.T("0", "l", "0", "xs");
        stack.Children.Add(moreTitle);
        bold.Checked += (_, _) => Change(style => style with { Bold = true });
        bold.Unchecked += (_, _) => Change(style => style with { Bold = false });
        band.Checked += (_, _) => Change(style => style with { BackgroundShape = SubtitleBackgroundShape.Band });
        band.Unchecked += (_, _) => Change(style => style with { BackgroundShape = SubtitleBackgroundShape.Fit });
        previous.Checked += (_, _) => Change(style => style with { StackPrevious = true });
        previous.Unchecked += (_, _) => Change(style => style with { StackPrevious = false });
        names.Checked += (_, _) => ChangeSettings(s => s with { ShowSpeakerNames = true });
        names.Unchecked += (_, _) => ChangeSettings(s => s with { ShowSpeakerNames = false });
        nameColors.Checked += (_, _) => ChangeSettings(s => s with { SpeakerColors = true });
        nameColors.Unchecked += (_, _) => ChangeSettings(s => s with { SpeakerColors = false });
        // Anahtar satırları ayar sayfalarındaki gibi aralıklı bir yığında (SwitchRow kendi çizgisini çizer).
        var switches = new SpacedStack();
        foreach (var row in new[] { bold, band, previous, names, nameColors }) switches.Children.Add(row);
        stack.Children.Add(switches);

        var test = ScreenKit.Button("TV'de dene", "OutlineButton", (_, _) => controller.SendTestSubtitle());
        test.Margin = Sp.T("0", "m", "0", "0");
        stack.Children.Add(test);
        return stack;
    }

    private void ChangeSettings(Func<UserSettings, UserSettings> change)
    {
        if (loading) return;
        controller.Settings = change(controller.Settings);
    }

    private static Border StepRow(string name, FrameworkElement control)
    {
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(control, Dock.Right);
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(control);
        var label = ScreenKit.Text(name);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        return ScreenKit.Row(row);
    }

    // ───── Gösterim ─────

    /// <summary>Kayıttaki biçimle her şeyi yeniler (karolar, sonuçlar, seçili hazır görünüm, değerler).</summary>
    private void Show()
    {
        loading = true;
        try
        {
            var style = CurrentStyle;
            foreach (var (stage, verdict, ground) in scenes)
            {
                DrawScene(stage);
                var result = SubtitleLegibility.Verdict(style, ground.Hex);
                verdict.Text = SubtitleLegibility.Label(result);
                verdict.SetResourceReference(TextBlock.ForegroundProperty, ScreenKit.VerdictBrush(result));
                AutomationProperties.SetName(verdict, $"{ground.Name}: {verdict.Text}");
            }
            ShowPresets(style);
            var font = Math.Clamp(style.FontSize, AppearanceSteps.MinFont, AppearanceSteps.MaxFont);
            fontStepper.Show(string.Create(CultureInfo.InvariantCulture, $"{style.FontSize} px"), font > AppearanceSteps.MinFont, font < AppearanceSteps.MaxFont);
            outlineStepper.Show(string.Create(CultureInfo.InvariantCulture, $"{style.OutlinePx} px"), style.OutlinePx > 0, style.OutlinePx < SubtitleStyle.MaxOutlineWidth);
            plateStepper.Show(string.Create(CultureInfo.InvariantCulture, $"%{Math.Clamp(style.Background, 0, 100)}"), style.Background > 0, style.Background < 100);
            var placements = new[] { SubtitlePlacement.Above, SubtitlePlacement.Cover, SubtitlePlacement.Fixed };
            var chosen = Array.IndexOf(placements, style.Placement);
            placementHost.Content = ScreenKit.Segmented("Konum", ["Üstünde", "Örtsün", "Sabit"], chosen,
                index => Change(s => s with { Placement = placements[index] }));
            fixedRow.Parent.SetValue(VisibilityProperty, style.Placement == SubtitlePlacement.Fixed ? Visibility.Visible : Visibility.Collapsed);
            fixedSlider.Value = Math.Clamp(style.BottomPercent, 0, 85);
            fixedValue.Text = string.Create(CultureInfo.InvariantCulture, $"%{Math.Clamp(style.BottomPercent, 0, 85)}");
            ShowSwatches(style);
            bold.IsChecked = style.Bold;
            band.IsChecked = style.BackgroundShape == SubtitleBackgroundShape.Band;
            previous.IsChecked = style.StackPrevious;
            names.IsChecked = controller.Settings.ShowSpeakerNames;
            nameColors.IsChecked = controller.Settings.SpeakerColors;
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>Hazır görünüm satırları: radyo halkası (seçiliyse 5 px sarı), ad ve bir satırlık açıklama.</summary>
    private void ShowPresets(SubtitleStyle style)
    {
        var match = AppearancePresets.Match(style);
        var focused = presetList.Children.OfType<Button>().FirstOrDefault(button => button.IsKeyboardFocused)?.Tag;
        presetList.Children.Clear();
        foreach (var info in AppearancePresets.All)
        {
            var on = info.Id == match;
            var radio = new Ellipse { Width = 16, Height = 16, StrokeThickness = on ? 5 : 1.5, VerticalAlignment = VerticalAlignment.Center };
            radio.SetResourceReference(Shape.StrokeProperty, on ? "amberBrush" : "faintBrush");
            var texts = new StackPanel { Margin = Sp.T("m", "0", "0", "0"), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(ScreenKit.Text(info.Name, 15, FontWeights.SemiBold));
            var desc = ScreenKit.Muted(info.Description, 13);
            desc.LineHeight = 18;
            texts.Children.Add(desc);
            var content = new DockPanel();
            DockPanel.SetDock(radio, Dock.Left);
            content.Children.Add(radio);
            content.Children.Add(texts);
            var button = new Button
            {
                Content = content, Tag = info.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 60, Padding = Sp.T("s", "xs", "s", "xs"), BorderThickness = new Thickness(0, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Template = RowTemplate(),
            };
            button.SetResourceReference(BorderBrushProperty, "borderBrush");
            if (on) button.SetResourceReference(BackgroundProperty, "fieldBrush");
            else button.Background = Brushes.Transparent;
            button.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
            AutomationProperties.SetName(button, $"{info.Name}. {info.Description}{(on ? " Seçili." : "")}");
            var id = info.Id;
            button.Click += (_, _) => Change(s => AppearancePresets.Apply(id, s));
            presetList.Children.Add(button);
            if (focused is AppearancePreset was && was == id) Dispatcher.BeginInvoke(() => button.Focus());
        }
    }

    /// <summary>Satır düğmesinin şablonu: kıl çizgi üstte, zemin seçiliyse alan rengi; üzerine gelince alan rengi.</summary>
    private static ControlTemplate RowTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Cell");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("surfaceRaisedBrush"), "Cell"));
        template.Triggers.Add(hover);
        return template;
    }

    /// <summary>Yazı rengi: dört açık renk; seçili olanın çevresinde sarı halka.</summary>
    private void ShowSwatches(SubtitleStyle style)
    {
        swatches.Children.Clear();
        foreach (var (name, hex) in TextColors)
        {
            var on = string.Equals(style.TextRgb, hex, StringComparison.OrdinalIgnoreCase);
            var dot = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(SpeakerText.Rgb(hex)), StrokeThickness = 1 };
            dot.SetResourceReference(Shape.StrokeProperty, "controlBorderBrush");
            var ring = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(on ? 2 : 0), Child = dot };
            ring.SetResourceReference(Border.BorderBrushProperty, "amberBrush");
            var button = new Button { Content = ring, Margin = Sp.T("xxs", "0", "0", "0"), Template = PlainTemplate(), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name };
            button.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
            AutomationProperties.SetName(button, $"Yazı rengi: {name}{(on ? ", seçili" : "")}");
            var chosen = hex;
            button.Click += (_, _) => Change(s => s with { TextHex = chosen, TextColor = chosen == "#FFE135" ? SubtitleTextColor.Yellow : SubtitleTextColor.White });
            swatches.Children.Add(button);
        }
    }

    private static ControlTemplate PlainTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        template.VisualTree = presenter;
        return template;
    }
}
