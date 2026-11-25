// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using Cevirgen.App.Controls;
using System.Windows.Controls;
using Cevirgen.Core;
using Cevirgen.Shell;

namespace Cevirgen.App.Views.Steps;

public partial class TestStep : UserControl
{
    private bool sent;
    /// <summary>Abone olunan denetleyici: sayfadan çıkınca DataContext boşalsa da aboneliği bununla kaldırırız.</summary>
    private AppController? subscribed;

    public TestStep()
    {
        InitializeComponent();
        // Perde önizlemesi: örnek Türkçe altyazı plakası (Mac DSPerdePreview).
        Preview.SetLine("Deniz:", "Hazır olduğunda haber ver, gidiyoruz.", 15);
        Loaded += (_, _) =>
        {
            if (DataContext is not AppController controller) return;
            ShowTarget();
            ShowTimeline();
            if (subscribed is not null) subscribed.PropertyChanged -= OnChanged;
            subscribed = controller;
            controller.PropertyChanged += OnChanged;
        };
        Unloaded += (_, _) =>
        {
            if (subscribed is not null) subscribed.PropertyChanged -= OnChanged;
            subscribed = null;
        };
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppController.TvClients) or nameof(AppController.TranslatorStatus) or nameof(AppController.Console)
            or nameof(AppController.ModelDownloading) or nameof(AppController.ModelProgress) or nameof(AppController.ModelError))
            Dispatcher.BeginInvoke(ShowTimeline);
    }

    /// <summary>
    /// Adım çizelgesi (Mac TryItStep): PS5 bağlantısı (ya da pencere / Xbox), çeviri çalışıyor, TV'de (ya da bu ekranda) altyazı;
    /// biten amber + tik, şimdiki amber halka.
    /// </summary>
    private void ShowTimeline()
    {
        // Sayfadan çıkıldıktan sonra kuyruktan gelen çağrı: DataContext boş olabilir (#1189).
        if (DataContext is not AppController) return;
        var text = Localizer.Current;
        var onComputer = Controller.Settings.Output == SubtitleOutput.Computer;
        var picture = Controller.Console is not null || Controller.IsXbox || Controller.CanStart;
        var translation = Controller.TranslatorStatus == TranslatorStatus.Ready;
        var shown = sent && (onComputer || Controller.TvClients > 0);
        var console = Controller.Console is { } registered ? (string.IsNullOrEmpty(registered.Name) ? "PS5" : registered.Name) : Controller.ConsoleTitle;
        var engine = Controller.Settings.Engine == TranslationEngine.Free ? text["Free Translation"] : Controller.Settings.Engine.DisplayName();
        ShowModel();
        var screen = Controller.Tv?.Name ?? text["This PC"];
        Timeline.Steps =
        [
            new(text[Controller.IsXbox ? "Xbox · Beta" : "PS5 Connection"], console, StepTimeline.Phase(picture, true)),
            new(text["Translation Working"], engine, StepTimeline.Phase(translation, picture)),
            new(text[onComputer ? "Subtitle on This Screen" : "TV Shows the Subtitle"], screen, StepTimeline.Phase(shown, picture && translation)),
        ];
    }

    private AppController Controller => (AppController)DataContext;

    /// <summary>
    /// Ücretsiz model hâlâ iniyorsa ilerleme; inemediyse ne yapılacağı ve "Yeniden dene" (çıkmaz yok). Model inince panel
    /// kalkar.
    /// </summary>
    private void ShowModel()
    {
        var installed = Controller.ModelInstalled || Controller.Settings.Engine != TranslationEngine.Free;
        ModelPanel.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        if (installed) return;
        var failed = Controller.ModelError is not null && !Controller.ModelDownloading;
        var percent = (int)System.Math.Round(Controller.ModelProgress * 100);
        ModelText.Text = failed ? "Ücretsiz çeviri modeli inemedi. İnternet bağlantını kontrol edip yeniden dene."
            : Controller.ModelDownloading ? $"Ücretsiz çeviri modeli iniyor: %{percent}. Bitince deneme altyazısı çevrilir."
            : "Ücretsiz çeviri modeli henüz inmedi.";
        ModelBar.Value = Controller.ModelProgress;
        ModelBar.Visibility = Controller.ModelDownloading ? Visibility.Visible : Visibility.Collapsed;
        ModelRetry.Visibility = Controller.ModelDownloading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRetryModel(object sender, RoutedEventArgs e) => Controller.StartModelDownload();

    /// <summary>TV seçilmediyse ("TV'm yok") örnek altyazı bu bilgisayarın ekranında görünür.</summary>
    private void ShowTarget()
    {
        if (Controller.Settings.Output != SubtitleOutput.Computer) return;
        var text = Localizer.Current;
        Layout.Message = text["Çevirgen will show a sample subtitle on this computer's screen, at the bottom."];
        Question.Title = text["Do you see it on the screen?"];
        Hint.Text = text["If you don't see it, send it again. It shows for a few seconds at the bottom of the screen."];
        Adjust.Visibility = Visibility.Visible;
        AutoOpenNote.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Çevirgen: deneme, TV uygulaması kapalıysa önce onu açar ve bağlanmasını (gerekirse eşleştirme onayını) bekler
    /// (<see cref="AppController.SendTestSubtitleAsync"/>). Eskiden yalnız gönderiyordu; TV'de Çevirgen kapalıysa hiçbir şey
    /// çıkmıyor, kullanıcı TV'de uygulamayı elle açması gerektiğini bilmiyordu (2026-11-25). Gitmezse ne yapılacağı yazılır.
    /// </summary>
    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var text = Localizer.Current;
        Send.IsEnabled = false;
        Theme.PerdeProps.SetBusy(Send, true);
        Send.Content = Controller.Settings.Output == SubtitleOutput.Computer ? Send.Content : "TV'de Çevirgen açılıyor…";
        try { await Controller.SendTestSubtitleAsync(); }
        catch (Exception error) { StartupGuard.Log("Deneme altyazısı gönderilemedi", error); }
        if (!IsLoaded) return;
        Theme.PerdeProps.SetBusy(Send, false);
        Send.IsEnabled = true;
        Send.Content = text["Send Test Subtitle"];
        if (Controller.Settings.Output != SubtitleOutput.Computer)
            Hint.Text = Controller.ShowTestTvHint
            ? "Deneme altyazısı TV'ye gitmedi: TV'deki Çevirgen bağlanmadı. TV'de Çevirgen uygulamasını aç. TV'de 6 haneli kod çıkarsa onu bir yere yazma; bu bilgisayarda açılan \"Bu TV eşleşsin mi?\" penceresinde onayla ve yeniden gönder."
            : text["If you don't see it, make sure the Çevirgen TV app is open and connect the TV again."];
        sent = true;
        Send.Style = (Style)FindResource("SecondaryButton");
        Question.Visibility = Visibility.Visible;
        ShowTimeline();
    }

    private void OnAdjust(object sender, RoutedEventArgs e) => Controller.OverlayAdjusting = true;

    private void OnYes(object sender, RoutedEventArgs e)
    {
        // Konumlandırılırken "Evet" denirse yeni yer kaydedilir (Bitti gibi).
        Controller.CommitOverlayEdit();
        Controller.Advance();
    }

    /// <summary>"Hayır": TV'deyse TV adımına dönülür (uygulama açılır, yeniden bağlanılır); bu ekrandaysa ipucu görünür, yeniden gönderilebilir.</summary>
    private void OnNo(object sender, RoutedEventArgs e)
    {
        if (Controller.Settings.Output == SubtitleOutput.Computer)
        {
            Send.Style = (Style)FindResource("PrimaryButton");
            Hint.Visibility = Visibility.Visible;
            return;
        }
        Controller.Step = OnboardingStep.Tv;
    }
}
