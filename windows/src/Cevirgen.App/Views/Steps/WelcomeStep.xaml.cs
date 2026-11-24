// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Cevirgen.App.Theme;
using Cevirgen.Shell;

namespace Cevirgen.App.Views.Steps;

/// <summary>
/// İlk açılış: "Oyunu nerede oynuyorsun?" (PS5 / Xbox / Bilgisayar / Diğer). Seçim denetleyicide bekler
/// (<see cref="AppController.PendingPlatform"/>); sihirbazın Devam'ı uygular. Ayarlar'dan "Platformu değiştir" ile de açılır.
/// </summary>
public partial class WelcomeStep : UserControl
{
    private readonly Dictionary<SetupPlatform, RadioButton> options = [];

    public WelcomeStep()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Build();
            Controller.PropertyChanged += OnChanged;
            // Açıklama ilk açılışta bir kez (kurulum Ayarlar'dan yeniden açıldıysa Ayarlar › Uygulama'da); anahtar burada yok.
            CountNote.Text = AppController.AnonymousCountOnboardingText;
            CountNote.Visibility = Controller.PartialSetup ? Visibility.Collapsed : Visibility.Visible;
        };
        Unloaded += (_, _) => Controller.PropertyChanged -= OnChanged;
    }

    private AppController Controller => (AppController)DataContext;

    /// <summary>Dört seçenek: ad (Archivo 26), nasıl çalıştığı, "Gerekenler" ve adım sayısı (sarı mono).</summary>
    private void Build()
    {
        Options.Children.Clear();
        options.Clear();
        var index = 0;
        foreach (var option in SetupPlatforms.All)
        {
            var body = new DockPanel { LastChildFill = false };
            var foot = new StackPanel();
            DockPanel.SetDock(foot, Dock.Bottom);
            var rule = new Border { Height = 1, Margin = Sp.T("0", "0", "0", "m") };
            rule.SetResourceReference(Border.BackgroundProperty, "borderBrush");
            foot.Children.Add(rule);
            // Seçilemeyen seçenek (yalnız gerçek bir neden varsa: bu derlemede Xbox bağlantısı yok) nedenini söyler.
            var unavailable = Unavailable(option.Id);
            foot.Children.Add(Label(unavailable is null ? "GEREKENLER" : "BU BİLGİSAYARDA YOK", unavailable is null ? "mutedBrush" : "warnBrush"));
            var needs = new TextBlock { Text = unavailable ?? option.Needs, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = Sp.T("0", "xxs", "0", "0") };
            needs.SetResourceReference(TextBlock.ForegroundProperty, "mutedBrush");
            foot.Children.Add(needs);
            var steps = Label(option.StepsText, "amberTextBrush");
            steps.Margin = Sp.T("0", "xs", "0", "0");
            foot.Children.Add(steps);
            body.Children.Add(foot);
            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            var name = new TextBlock { Text = option.Name, FontSize = 26 };
            name.SetResourceReference(StyleProperty, "ScreenTitle");
            name.FontSize = 26;
            top.Children.Add(name);
            var how = new TextBlock { Text = option.How, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = Sp.T("0", "xs", "0", "m"), LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            how.SetResourceReference(TextBlock.ForegroundProperty, "subduedBrush");
            top.Children.Add(how);
            body.Children.Add(top);
            var button = new RadioButton { Content = body, Tag = option.Id, VerticalContentAlignment = VerticalAlignment.Stretch, BorderThickness = new Thickness(index == 0 ? 0 : 1, 0, 0, 0) };
            button.SetResourceReference(StyleProperty, "PlatformOption");
            System.Windows.Automation.AutomationProperties.SetName(button, $"{option.Name}. {option.How} Gerekenler: {option.Needs}. {option.StepsText}");
            // İlk açılışta dört seçenek de seçilebilir görünür; yalnız Xbox bağlantısı bu derlemede yoksa Xbox soluk ve nedeni yazılı.
            button.IsEnabled = unavailable is null;
            if (unavailable is not null)
            {
                button.ToolTip = unavailable;
                ToolTipService.SetShowOnDisabled(button, true);
            }
            button.IsChecked = option.Id == Controller.PendingPlatform;
            button.Checked += (_, _) => Controller.ChoosePlatform(option.Id);
            options[option.Id] = button;
            Options.Children.Add(button);
            index++;
        }
    }

    /// <summary>Seçeneğin bu bilgisayarda seçilememe nedeni; seçilebiliyorsa null.</summary>
    private string? Unavailable(SetupPlatform platform) =>
        platform == SetupPlatform.Xbox && !Controller.XboxAvailable
            ? "Bu sürümde Xbox bağlantısı yok. Xbox uygulamasında ya da yakalama kartıyla oynuyorsan Diğer'i seç."
            : null;

    private static TextBlock Label(string text, string brush)
    {
        var label = new TextBlock { Text = text };
        label.SetResourceReference(StyleProperty, "MonoLabel");
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return label;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppController.PendingPlatform) && options.TryGetValue(Controller.PendingPlatform, out var button))
            button.IsChecked = true;
    }
}
