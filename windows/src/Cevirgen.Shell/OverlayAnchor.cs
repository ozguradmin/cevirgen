// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Çevirgen: bilgisayar ekranındaki altyazı penceresinin İngilizce satıra göre yeri. Pencere eskiden hep kullanıcının
/// koyduğu yerde (varsayılan: ekranın altında) duruyordu; "Örtsün" yalnız kutuyu İngilizce satır kadar büyütüyor,
/// "Üstünde"/"Altında" hiç uygulanmıyordu: Life Is Strange'de çeviri İngilizcenin biraz üstünde kalıyor, İngilizce altından
/// görünüyordu (2026-11-25). Kurallar TV sayfasındaki layout() ile aynı: "Örtsün"de çevirinin kutusu İngilizce satırın
/// ortasına, "Üstünde"/"Altında"da arada oyun yüksekliğinin %2,4'ü boşluk; ekranın kenarında yer yoksa öbür yana geçilir;
/// yatayda İngilizce satırın ortasına hizalanır.
/// </summary>
public static class OverlayAnchor
{
    /// <summary>"Üstünde"/"Altında"da çeviri ile İngilizce satır arasındaki boşluk (oyun yüksekliğinin oranı; TV'de PLACE_GAP).</summary>
    public const double Gap = 0.024;
    /// <summary>İngilizce satır bu kadar alttaysa "Altında" yerine üstüne konur; bu kadar üstteyse "Üstünde" yerine altına.</summary>
    public const double EdgeShare = 0.12;

    /// <summary>
    /// Pencerenin sol üst köşesi (WPF birimiyle). <paramref name="english"/> İngilizce satırın karedeki yeri (0–1),
    /// <paramref name="game"/> karenin ekrandaki yeri, <paramref name="line"/> çevirinin kutusunun pencere içindeki yeri.
    /// Yerleşim "Sabit"se, kutu boşsa ya da altyazı olamayacak kadar büyükse (karenin yarısından fazla) null: pencere
    /// kullanıcının koyduğu yerde kalır.
    /// </summary>
    public static (double Left, double Top)? WindowPosition(SubtitlePlacement placement, TextBox english, ScreenArea game,
        ScreenArea line)
    {
        if (placement == SubtitlePlacement.Fixed || !game.IsUsable || !(line.Width > 0) || !(line.Height > 0)) return null;
        if (!(english.Right > english.Left) || !(english.Bottom > english.Top) || english.Bottom - english.Top > 0.5) return null;
        var mode = placement;
        if (mode == SubtitlePlacement.Below && english.Bottom > 1 - EdgeShare) mode = SubtitlePlacement.Above;
        if (mode == SubtitlePlacement.Above && english.Top < EdgeShare) mode = SubtitlePlacement.Below;

        var top = game.Top + english.Top * game.Height;
        var bottom = game.Top + english.Bottom * game.Height;
        var center = game.Left + (english.Left + english.Right) / 2 * game.Width;
        var gap = Math.Round(game.Height * Gap);
        var left = center - (line.Left + line.Width / 2);
        var windowTop = mode switch
        {
            SubtitlePlacement.Cover => (top + bottom) / 2 - (line.Top + line.Height / 2),
            SubtitlePlacement.Above => top - gap - (line.Top + line.Height),
            _ => bottom + gap - line.Top,
        };
        return double.IsFinite(left) && double.IsFinite(windowTop) ? (Math.Round(left), Math.Round(windowTop)) : null;
    }
}
