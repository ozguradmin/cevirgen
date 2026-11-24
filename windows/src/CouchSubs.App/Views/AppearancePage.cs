// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>
/// Görünüm sekmesi (1. bölüm): başlık ve eski altyazı görünümü düzenleyicisi (<see cref="AppearanceEditor"/>; yazı boyu,
/// zemin, renkler). 2. bölüm bunu taslaktaki okunurluk testi ekranıyla (Appearance.dc.html) değiştirecek; TV'deki yer ve
/// kenarlık ayarları şimdilik Ayarlar › Gelişmiş › Görünüm (geçici) bölümünde.
/// </summary>
public sealed class AppearancePage : UserControl
{
    public AppearancePage(AppController controller)
    {
        DataContext = controller;
        var stack = new StackPanel { Margin = Sp.T("edge", "l", "edge", "l") };
        var title = new TextBlock { Text = "Görünüm" };
        title.SetResourceReference(StyleProperty, "ScreenTitle");
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(title, System.Windows.Automation.AutomationHeadingLevel.Level1);
        stack.Children.Add(title);
        var hint = new TextBlock
        {
            Text = "Altyazının TV'de, bilgisayarda ve telefonda nasıl göründüğü. Yer, kenarlık ve önceki satırlar: Ayarlar › Gelişmiş.",
            Margin = Sp.T("0", "titleBody", "0", "l"),
        };
        hint.SetResourceReference(StyleProperty, "HintText");
        stack.Children.Add(hint);
        var link = new Button { Content = "Yer ve kenarlık ayarları", HorizontalAlignment = HorizontalAlignment.Left, Margin = Sp.T("0", "0", "0", "l") };
        link.SetResourceReference(StyleProperty, "TextLink");
        link.Click += (_, _) => ShellWindow.Navigate(SettingsGroup.Advanced);
        stack.Children.Add(link);
        stack.Children.Add(new AppearanceEditor(controller));
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            Content = stack,
        };
    }
}
