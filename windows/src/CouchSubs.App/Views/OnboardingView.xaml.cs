// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CouchSubs.App.Controls;
using CouchSubs.App.Theme;
using CouchSubs.App.Views.Steps;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>Kurulum sihirbazı: üstte adım sayacı ve ilerleme, ortada adım, altta Geri / Devam.</summary>
public partial class OnboardingView : UserControl
{
    private readonly AppController controller;

    public OnboardingView(AppController controller)
    {
        this.controller = controller;
        DataContext = controller;
        InitializeComponent();
        controller.PropertyChanged += OnChanged;
        Unloaded += (_, _) => controller.PropertyChanged -= OnChanged;
        Loaded += (_, _) => Ambient.Visibility = Window.GetWindow(this) is ShellWindow ? Visibility.Collapsed : Visibility.Visible;
        ShowStep();
        ShowIntro();
    }

    /// <summary>Açılış bu oturumda gösterildi (yalnız ilk kurulumun karşılama adımında, bir kez).</summary>
    private static bool introShown;

    /// <summary>İlk kurulumun karşılama adımında açılış sihirbazın üstünde oynar; "Hadi Başlayalım" ya da "Atla" ile söner.</summary>
    private void ShowIntro()
    {
        if (introShown || controller.Step != OnboardingStep.Welcome || controller.DemoMode) return;
        introShown = true;
        var wizard = (UIElement)Content;
        Content = null;
        var stage = new Grid();
        stage.Children.Add(wizard);
        IntroView? intro = null;
        intro = new IntroView(() =>
        {
            var fade = Theme.Motion.To(0, Theme.Motion.Page, exit: true);
            fade.Completed += (_, _) => stage.Children.Remove(intro);
            intro!.BeginAnimation(OpacityProperty, fade);
        }, controller.Settings.StartupSound);
        stage.Children.Add(intro);
        Content = stage;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppController.Step)) ShowStep();
        else if (e.PropertyName == nameof(AppController.ConsoleKind))
        {
            ShowSteps();
        }
        else if (e.PropertyName == nameof(AppController.UsesFreeGemini)) ShowContinue();
        else if (e.PropertyName is nameof(AppController.PendingPlatform) or nameof(AppController.PartialSetup)) ShowSteps();
        else if (e.PropertyName is nameof(AppController.Tv) or nameof(AppController.HasTv)) ShowSteps();
        else if (e.PropertyName is nameof(AppController.ModelDownloading)) ShowSteps();
    }

    /// <summary>
    /// "Daha iyi çeviri" isteğe bağlıdır: anahtar yokken alttaki düğme ikincil "Şimdilik geç" (birincil düğme adımdaki
    /// "Ücretsiz anahtar al"), anahtar kaydedilince birincil "Devam".
    /// </summary>
    private void ShowContinue()
    {
        var skip = controller.Step == OnboardingStep.BetterTranslation && !controller.UsesFreeGemini;
        ContinueButton.Content = Localizer.Current[skip ? "Skip for Now" : "Continue"];
        ContinueButton.Style = (Style)FindResource(skip ? "SecondaryButton" : "WidePrimaryButton");
        // Anahtar alanında Enter adımı geçmesin.
        ContinueButton.IsDefault = !skip;
    }

    private void ShowStep()
    {
        StepHost.Content = controller.Step switch
        {
            OnboardingStep.Welcome => new WelcomeStep(),
            OnboardingStep.SecondAccount => new SecondAccountStep(),
            OnboardingStep.PsnLogin => controller.IsXbox ? new XboxLoginStep() : new PsnLoginStep(),
            OnboardingStep.Console => controller.IsXbox ? new XboxConsoleStep() : new ConsoleStep(),
            OnboardingStep.Tv => new TvStep(),
            OnboardingStep.Hdmi => new HdmiStep(),
            OnboardingStep.Translation => new TranslationStep(),
            OnboardingStep.Test => new TestStep(),
            OnboardingStep.Window => new WindowStep(),
            _ => new BetterTranslationStep(),
        };
        ShowSteps();
        // Adım 8 px aşağıdan kayarak gelir (hareket azaltılmışsa yalnız solma).
        StepScroll.ScrollToTop();
        if (StepHost.Content is FrameworkElement page) Theme.Motion.EnterFrom(page, 12, Theme.Motion.Page);
        BackButton.Visibility = controller.Step == OnboardingStep.Welcome ? Visibility.Collapsed : Visibility.Visible;
        ContinueButton.Visibility = controller.Step == OnboardingStep.Test ? Visibility.Hidden : Visibility.Visible;
        ShowContinue();
    }

    /// <summary>
    /// Adım etiketi ("Adım 2 / 4 · Hesap") ve üstteki ilerleme noktaları: seçilen platformun gerçek adım sayısı
    /// (<see cref="SetupPlatforms.Progress"/>; TV girişi TV adımıyla bir sayılır). Şimdiki adım 24 genişlikte sarı hap,
    /// geçilenler soluk sarı, sıradakiler çerçeve renginde nokta.
    /// </summary>
    private void ShowSteps()
    {
        var text = Localizer.Current;
        var platform = controller.Step == OnboardingStep.Welcome ? controller.PendingPlatform : controller.Platform;
        var (number, count) = SetupPlatforms.Progress(platform, controller.Step);
        var name = controller.Step switch
        {
            OnboardingStep.SecondAccount or OnboardingStep.PsnLogin => "Hesap",
            OnboardingStep.Console => controller.IsXbox ? "Xbox" : "PS5",
            OnboardingStep.Window => "Pencere",
            OnboardingStep.Tv => "TV",
            OnboardingStep.Hdmi => "TV girişi",
            OnboardingStep.Translation or OnboardingStep.BetterTranslation => "Çeviri",
            OnboardingStep.Test => "Deneme",
            _ => "",
        };
        Counter.Text = $"Adım {number} / {count}" + (name.Length > 0 ? $" · {name}" : "");
        // Platform sorusunda sayaç yok (taslak); notta seçilen platform ve kurulumun uzunluğu.
        Counter.Visibility = controller.Step == OnboardingStep.Welcome ? Visibility.Collapsed : Visibility.Visible;
        var picked = SetupPlatforms.Option(controller.PendingPlatform);
        Footnote.Text = controller.Step == OnboardingStep.Welcome ? $"Seçilen: {picked.Name} · {picked.StepsText}"
            : FootnoteKey() is { } key ? text[key] : "";
        // Ayarlar'dan açılan kurulumda "Sonra Ayarlarım" yerine "Vazgeç" (ana ekrana döner).
        LaterButton.Content = controller.PartialSetup ? "Vazgeç" : "Sonra kurarım";
        Footnote.Visibility = Footnote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Dots.Children.Clear();
        // Karşılama dahil: 1 platform, sonra platformun adımları.
        for (var dot = 0; dot <= count; dot++)
        {
            var current = dot == number;
            var element = new Border
            {
                Width = current ? 24 : 8, Height = 8, CornerRadius = new CornerRadius(4),
                Margin = new Thickness(dot == 0 ? 0 : Sp.V("xs"), 0, 0, 0),
            };
            element.SetResourceReference(Border.BackgroundProperty, current ? "amberBrush" : dot < number ? "amberLineBrush" : "borderBrush");
            if (dot < number) element.Opacity = 0.6;
            Dots.Children.Add(element);
        }
        System.Windows.Automation.AutomationProperties.SetName(Dots, Counter.Text);
    }

    /// <summary>Alt çubuktaki kısa not (Mac WizardText.footnote; Mac'e özgü anahtar zinciri notu Windows'ta yok).</summary>
    private string? FootnoteKey() => controller.Step switch
    {
        OnboardingStep.Window => "Yalnız seçilen pencere okunur; sonra Ayarlar › Cihazlar'dan değiştirebilirsin.",
        OnboardingStep.Test when controller.ModelDownloading => "Ücretsiz çeviri modeli arka planda iniyor; bitince deneme çalışır.",
        OnboardingStep.PsnLogin when !controller.IsXbox => "If sign-in opens in the browser, finish it there; the app continues by itself.",
        OnboardingStep.Console when !controller.IsXbox => "If the PS5 isn't found, tips open after 10 seconds.",
        OnboardingStep.Tv => controller.HasTv
            ? "If the installation continues in the background, it shows on the home screen when it's done."
            : "Approve the code on the TV to continue. If you don't have a TV, skip with \"I Don't Have a TV\".",
        OnboardingStep.Hdmi => "Confirm the picture to continue.",
        _ => null,
    };

    /// <summary>"Sonra Ayarlarım": kurulum yarıda bırakılır, ana ekran "Kurulum Eksik" durumuyla açılır.</summary>
    private void OnLater(object sender, RoutedEventArgs e)
    {
        if (controller.PartialSetup) controller.CancelPartialSetup();
        else ShellWindow.DeferSetup();
    }

    private void OnBack(object sender, RoutedEventArgs e) => controller.Back();

    private void OnReset(object sender, RoutedEventArgs e)
    {
        controller.ResetSetup();
        ShowStep();
    }

    /// <summary>Devam: karşılamada seçilen platform uygulanır, sihirbaz o platformun sıradaki adımına geçer.</summary>
    private void OnContinue(object sender, RoutedEventArgs e) => controller.Advance();
}
