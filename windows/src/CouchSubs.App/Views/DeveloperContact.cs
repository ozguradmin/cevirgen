// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CouchSubs.App.Theme;
using CouchSubs.Core;

namespace CouchSubs.App.Views;

/// <summary>
/// Çevirgen: geliştiricinin iletişim satırları (Ayarlar › Uygulama › Hakkında, Hakkında penceresi, Ayarlar'ın alt bilgisi):
/// "Destek: destek@ozgurguler.tech" (tıklayınca adres panoya kopyalanır, kısa "Kopyalandı"; yanında "E-posta gönder" mailto:)
/// ve "Geliştiren: ozgurguler.tech" (https://ozgurguler.tech/professional).
/// </summary>
public sealed class DeveloperContact : StackPanel
{
    public const string SupportEmail = "destek@ozgurguler.tech";
    public const string DeveloperSite = "ozgurguler.tech";
    public const string DeveloperUrl = "https://ozgurguler.tech/professional";

    private readonly TextBlock copied;
    private DispatcherTimer? hide;

    /// <param name="centered">Hakkında penceresinde ortalı.</param>
    /// <param name="compact">Alt bilgide: yalnız destek satırı, küçük ve soluk.</param>
    public DeveloperContact(bool centered = false, bool compact = false)
    {
        var align = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        HorizontalAlignment = align;

        var support = new WrapPanel { HorizontalAlignment = align };
        support.Children.Add(Label("Destek:", compact));
        var mail = Link(SupportEmail, compact, $"Adresi kopyala ({SupportEmail})");
        mail.Click += (_, _) => Copy();
        support.Children.Add(mail);
        if (!compact)
        {
            var send = Link("E-posta gönder", compact, $"mailto:{SupportEmail}");
            send.Margin = Sp.T("s", "0", "0", "0");
            send.Click += (_, _) => Open($"mailto:{SupportEmail}");
            support.Children.Add(send);
        }
        copied = new TextBlock { Text = "Kopyalandı", Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("s", "0", "0", "0") };
        copied.SetResourceReference(StyleProperty, "DoneText");
        if (compact) copied.FontSize = 11;
        support.Children.Add(copied);
        Children.Add(support);

        if (compact) return;
        var developer = new WrapPanel { HorizontalAlignment = align };
        developer.Children.Add(Label("Geliştiren:", compact));
        var site = Link(DeveloperSite, compact, DeveloperUrl);
        site.Click += (_, _) => Open(DeveloperUrl);
        developer.Children.Add(site);
        Children.Add(developer);
    }

    private static TextBlock Label(string text, bool compact)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("0", "0", "xxs", "0") };
        label.SetResourceReference(StyleProperty, "HintText");
        if (compact) label.FontSize = 12;
        return label;
    }

    private static Button Link(string text, bool compact, string tip)
    {
        var button = new Button { Content = text, ToolTip = tip };
        button.SetResourceReference(StyleProperty, "TextLink");
        if (compact)
        {
            button.FontSize = 12;
            button.SetResourceReference(Control.ForegroundProperty, "mutedBrush");
        }
        return button;
    }

    /// <summary>Adres panoya; iki saniye "Kopyalandı".</summary>
    private void Copy()
    {
        try { Clipboard.SetText(SupportEmail); }
        catch (System.Runtime.InteropServices.ExternalException error)
        {
            SubtitleLog.Write($"destek adresi kopyalanamadı: {error.GetType().Name}");
            return;
        }
        copied.Visibility = Visibility.Visible;
        hide?.Stop();
        hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        hide.Tick += (_, _) =>
        {
            hide?.Stop();
            copied.Visibility = Visibility.Collapsed;
        };
        hide.Start();
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SubtitleLog.Write($"bağlantı açılamadı: {error.GetType().Name}");
        }
    }
}
