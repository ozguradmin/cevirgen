// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>Önizlemede altyazı bloğunun yeri (önizleme yüksekliğine oranla; biri dolu).</summary>
/// <param name="Bottom">Bloğun altı, alttan (ör. 0.07 = alttan %7).</param>
/// <param name="Top">Bloğun üstü, üstten.</param>
/// <param name="Center">Bloğun ortası, üstten ("Örtsün").</param>
/// <param name="Left">Sol kenar boşluğu (genişliğe oranla).</param>
/// <param name="Right">Sağ kenar boşluğu.</param>
public sealed record PreviewAnchor(double? Bottom, double? Top, double? Center, double Left, double Right);

/// <summary>
/// Çevirgen: Canlı ekrandaki TV önizlemesinin altyazı yerleşimi. TV uygulamasının (tv-webos/app.js › layout, applyStyle,
/// outlineShadow) kurallarının aynısı, 1920 × 1080 TV ekranından önizlemenin genişliğine ölçeklenir: yazı boyu
/// <c>FontSize × genişlik / 1920</c>; üst satırlar %80; kenarlık <see cref="SubtitleStyle.OutlineWidth"/> TV pikseli.
/// Oyun karesi de aynı genişliğe sığdırılır (<see cref="FrameRect"/>): karedeki İngilizce ile Türkçe arasındaki oran TV'deki
/// gibidir. Arayüzden bağımsızdır; test edilir.
/// </summary>
public static class LivePreviewLayout
{
    /// <summary>TV'nin mantıksal genişliği (yazı boyu bu genişliğe göre).</summary>
    public const double TvWidth = 1920;
    /// <summary>İngilizce satırla çeviri arasındaki boşluk (yüksekliğin %1,2'si; TV ile aynı).</summary>
    public const double Gap = 0.012;
    /// <summary>Yanlarda en az bu kadar boşluk (%5; TV'deki güvenli alan).</summary>
    public const double SideMin = 0.05;

    /// <summary>Önizleme ölçeği: genişlik / 1920 (boş ya da bozuk genişlikte 0).</summary>
    public static double Scale(double previewWidth) => double.IsFinite(previewWidth) && previewWidth > 0 ? previewWidth / TvWidth : 0;

    /// <summary>Satırın yazı boyu (px): güncel satır ayardaki boy, üstteki eski satırlar %80 (TV: subtitle-prev).</summary>
    public static double FontPx(SubtitleStyle style, double previewWidth, bool current = true) =>
        OverlayPlacement.FontSize(style.FontSize) * Scale(previewWidth) * (current ? 1 : 0.8);

    /// <summary>
    /// Yazı kenarlığının kalınlığı (px): ayardaki kalınlık (TV pikseli, <see cref="SubtitleStyle.OutlinePx"/>), önizlemeye
    /// ölçekli; üstteki küçük satırlarda TV'deki gibi max(1, round(k × 0,8)). Kapalıysa 0.
    /// </summary>
    public static double OutlinePx(SubtitleStyle style, double previewWidth, bool current = true)
    {
        var width = style.OutlinePx;
        if (width <= 0) return 0;
        var tv = current ? width : Math.Max(1, Math.Round(width * 0.8, MidpointRounding.AwayFromZero));
        return tv * Scale(previewWidth);
    }

    /// <summary>
    /// Oyun karesinin önizlemedeki yeri: kare (<paramref name="frameWidth"/> × <paramref name="frameHeight"/> piksel) oranı
    /// korunarak önizlemeye sığdırılır ve ortalanır (16:9 olmayan pencere karesinde yanlarda ya da üstte boşluk kalır;
    /// TV de böyle gösterir). Kare yoksa bütün önizleme. Türkçe satırlar bu alanın genişliğine göre ölçeklenir.
    /// </summary>
    public static (double X, double Y, double Width, double Height) FrameRect(int frameWidth, int frameHeight, double previewWidth, double previewHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || previewWidth <= 0 || previewHeight <= 0) return (0, 0, Math.Max(0, previewWidth), Math.Max(0, previewHeight));
        var scale = Math.Min(previewWidth / frameWidth, previewHeight / frameHeight);
        var width = frameWidth * scale;
        var height = frameHeight * scale;
        return ((previewWidth - width) / 2, (previewHeight - height) / 2, width, height);
    }

    /// <summary>Karedeki bir pikselin önizlemedeki boyu (kare → önizleme); kare yoksa 0.</summary>
    public static double FrameScale(int frameWidth, int frameHeight, double previewWidth, double previewHeight) =>
        frameWidth <= 0 || frameHeight <= 0 ? 0 : FrameRect(frameWidth, frameHeight, previewWidth, previewHeight).Width / frameWidth;

    /// <summary>Zeminin saydamlığı (0–1; ayardaki koyuluk %).</summary>
    public static double BackgroundAlpha(SubtitleStyle style) => Math.Clamp(style.Background, 0, 100) / 100.0;

    /// <summary>
    /// Bloğun yeri. Oyundaki İngilizce satırın yeri (<paramref name="english"/>, kareye oranla) biliniyorsa yerleşime göre:
    /// üstünde (yer yoksa altında), altında (yer yoksa üstünde) ya da ortasında ("Örtsün"); yoksa ya da "Sabit"te alttan
    /// <see cref="SubtitleStyle.BottomPercent"/>. Yatayda İngilizce satırın ortasına hizalanır, yanlarda en az %5 kalır.
    /// </summary>
    public static PreviewAnchor Anchor(SubtitleStyle style, TextBox? english)
    {
        if (style.Placement == SubtitlePlacement.Fixed || english is not { } box || !Usable(box))
            return new PreviewAnchor(Math.Clamp(style.BottomPercent, 0, 85) / 100.0, null, null, 0.08, 0.08);
        var center = (box.Left + box.Right) / 2;
        var half = Math.Max(0.3, Math.Min(center, 1 - center));
        var left = Math.Max(SideMin, center - half);
        var right = Math.Max(SideMin, 1 - center - half);
        var mode = style.Placement;
        if (mode == SubtitlePlacement.Below && box.Bottom > 0.88) mode = SubtitlePlacement.Above;
        if (mode == SubtitlePlacement.Above && box.Top < 0.12) mode = SubtitlePlacement.Below;
        return mode switch
        {
            SubtitlePlacement.Cover => new PreviewAnchor(null, null, (box.Top + box.Bottom) / 2, left, right),
            SubtitlePlacement.Above => new PreviewAnchor(1 - box.Top + Gap, null, null, left, right),
            _ => new PreviewAnchor(null, box.Bottom + Gap, null, left, right),
        };
    }

    /// <summary>Okuma alanından (ayarlardaki altyazı alanı) İngilizce satırın tahmini yeri: alanın kendisi.</summary>
    public static TextBox? FromRegion(CropRegion region) =>
        region.Right > region.Left && region.Bottom > region.Top ? new TextBox(region.Left, region.Top, region.Right, region.Bottom) : null;

    /// <summary>
    /// Görünümün kısa özeti (Canlı ekrandaki "Görünüm" bağlantısı): "İngilizcenin üstünde · 32 px · kenarlık 2 px · zemin %40".
    /// </summary>
    public static string Summary(SubtitleStyle style)
    {
        var place = style.Placement switch
        {
            SubtitlePlacement.Cover => "İngilizcenin üstüne",
            SubtitlePlacement.Above => "İngilizcenin üstünde",
            SubtitlePlacement.Below => "İngilizcenin altında",
            _ => "sabit yerde",
        };
        var parts = new List<string> { place, string.Create(CultureInfo.InvariantCulture, $"{OverlayPlacement.FontSize(style.FontSize):0} px") };
        if (style.OutlinePx > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"kenarlık {style.OutlinePx} px"));
        if (style.Background > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"zemin %{Math.Clamp(style.Background, 0, 100)}"));
        return string.Join(" · ", parts);
    }

    private static bool Usable(TextBox box) =>
        double.IsFinite(box.Left) && double.IsFinite(box.Top) && double.IsFinite(box.Right) && double.IsFinite(box.Bottom)
        && box.Right > box.Left && box.Bottom > box.Top;
}
