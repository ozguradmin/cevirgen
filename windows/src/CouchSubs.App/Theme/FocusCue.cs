// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Input;

namespace CouchSubs.App.Theme;

/// <summary>
/// Odak halkası yalnız klavyeyle gezinirken (Tab, oklar) görünür; fareyle, dokunarak ya da kalemle tıklayınca hemen kalkar.
/// WPF'nin kendi kuralı (son giriş klavye mi) Alt+Tab ya da bir kısayoldan sonra tıklamada da halka çizdiriyordu: üst sekmeye
/// tıklayınca sekmeden taşan sarı kutu çıkıyordu. Değer pencereye yazılır ve içeriğe miras geçer; odak halkası stilleri
/// (Styles.xaml › FocusRing*) görünürlüğünü buradan alır.
/// </summary>
public static class FocusCue
{
    public static readonly DependencyProperty VisibleProperty = DependencyProperty.RegisterAttached(
        "Visible", typeof(bool), typeof(FocusCue), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetVisible(DependencyObject element) => (bool)element.GetValue(VisibleProperty);
    public static void SetVisible(DependencyObject element, bool value) => element.SetValue(VisibleProperty, value);

    private static bool installed;
    private static bool visible;

    /// <summary>Uygulama açılışında bir kez.</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            switch (e.StagingItem.Input)
            {
                case MouseButtonEventArgs or TouchEventArgs or StylusDownEventArgs:
                    Show(false);
                    break;
                case KeyEventArgs key when key.RoutedEvent == Keyboard.PreviewKeyDownEvent && IsNavigation(key.Key == Key.System ? key.SystemKey : key.Key):
                    Show(true);
                    break;
            }
        };
    }

    /// <summary>Odağı taşıyan tuşlar (yazı yazmak halkayı açmaz).</summary>
    public static bool IsNavigation(Key key) => key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down;

    private static void Show(bool value)
    {
        if (visible == value) return;
        visible = value;
        if (Application.Current is not { } app) return;
        foreach (Window window in app.Windows) SetVisible(window, value);
    }

    /// <summary>Yeni açılan pencere şu anki durumla başlar.</summary>
    public static void Apply(Window window) => SetVisible(window, visible);
}
