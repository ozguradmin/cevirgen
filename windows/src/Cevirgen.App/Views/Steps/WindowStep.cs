// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Controls;
using Cevirgen.App.Controls;
using Cevirgen.App.Theme;
using Cevirgen.Shell;

namespace Cevirgen.App.Views.Steps;

/// <summary>
/// Çevirgen kurulumu, Bilgisayar ve Diğer platformu: okunacak pencerenin seçimi (Ayarlar › Cihazlar › Görüntü kaynağı'ndaki
/// pencere listesinin aynısı). Yalnız seçilen pencere okunur; pencere kapanırsa masaüstünün tamamına geçilmez. Seçilmeden
/// Devam edilemez.
/// </summary>
public sealed class WindowStep : UserControl
{
    private readonly StackPanel list = new();
    private readonly TextBlock empty = new()
    {
        Text = "Uygun pencere bulunamadı. Önce oyunu ya da OBS'i aç, sonra yenile.", Visibility = Visibility.Collapsed,
    };
    private readonly Callout projectorHint = new()
    {
        Text = "OBS kullanıyorsan yakalama kartı kaynağına sağ tıkla › Kaynak Projektörü Aç › Yeni pencere; o pencereyi seç. " +
               "Program ve Önizleme projektörleri Çevirgen'in kendi altyazısını da içerir.",
        Visibility = Visibility.Collapsed,
    };
    private bool loading;

    public WindowStep()
    {
        empty.SetResourceReference(StyleProperty, "HintText");
        Loaded += (_, _) => Build();
    }

    private AppController Controller => (AppController)DataContext;

    private void Build()
    {
        var capture = Controller.Platform == SetupPlatform.Capture;
        var refresh = new Button { Content = "Pencereleri yenile", HorizontalAlignment = HorizontalAlignment.Left, Margin = Sp.T("0", "m", "0", "0") };
        refresh.SetResourceReference(StyleProperty, "OutlineButton");
        refresh.Click += (_, _) => Fill();
        var stack = new StackPanel();
        stack.Children.Add(list);
        stack.Children.Add(empty);
        stack.Children.Add(refresh);
        projectorHint.Margin = Sp.T("0", "m", "0", "0");
        stack.Children.Add(projectorHint);
        Content = new StepLayout
        {
            Title = "Hangi pencereyi okuyalım?",
            Message = capture
                ? "Yakalama kartının görüntüsünü bilgisayarda aç: OBS'te Kaynak Projektörü ya da kartın kendi uygulaması. Sonra o pencereyi seç."
                : "Oyunu pencereli ya da kenarlıksız tam ekran aç, sonra listeden seç. Türkçe oyunun üstünde ya da TV'de çıkar.",
            Content = stack,
        };
        Fill();
    }

    /// <summary>Açık pencereler seçenek kartı olarak (ad ve uygulama); kayıtlı pencere listede yoksa başta kalır.</summary>
    private void Fill()
    {
        loading = true;
        list.Children.Clear();
        var saved = Controller.Settings.CaptureWindow;
        var windows = Controller.AvailableCaptureWindows().ToList();
        if (saved is not null && !windows.Contains(saved)) windows.Insert(0, saved);
        foreach (var target in windows)
        {
            var card = new OptionCard
            {
                Content = target.Title, Description = target.ProcessName, GroupName = "captureWindow", Margin = Sp.T("0", "0", "0", "xs"),
                IsChecked = target == saved,
            };
            card.Checked += (_, _) =>
            {
                if (loading) return;
                Controller.Settings = Controller.Settings with { CaptureWindow = target };
                projectorHint.Visibility = UsagePlatform.IsCaptureSource(target) ? Visibility.Visible : Visibility.Collapsed;
            };
            list.Children.Add(card);
        }
        empty.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        projectorHint.Visibility = UsagePlatform.IsCaptureSource(saved) ? Visibility.Visible : Visibility.Collapsed;
        loading = false;
    }
}
