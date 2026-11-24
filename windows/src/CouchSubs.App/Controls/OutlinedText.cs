// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CouchSubs.App.Controls;

/// <summary>
/// Kenarlıklı yazı (TV'deki "Yazı kenarlığı": yazının çevresinde düz siyah kontur). WPF TextBlock kontur çizmez; yazı
/// geometriye çevrilir, önce kalın yuvarlak uçlu kalemle kontur, üstüne dolgu çizilir. Satırlar ortalanır ve sarar.
/// Canlı ekrandaki TV önizlemesi kullanır.
/// </summary>
public sealed class OutlinedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedText),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(OutlinedText),
        new FrameworkPropertyMetadata(FontWeights.SemiBold, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(OutlinedText),
        new FrameworkPropertyMetadata(new FontFamily("Segoe UI"), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(OutlinedText),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(OutlinedText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Konturun yazının dışına taşan kalınlığı (px); 0 ise kontur yok.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LineHeightFactorProperty = DependencyProperty.Register(nameof(LineHeightFactor), typeof(double), typeof(OutlinedText),
        new FrameworkPropertyMetadata(1.3, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double LineHeightFactor { get => (double)GetValue(LineHeightFactorProperty); set => SetValue(LineHeightFactorProperty, value); }

    private FormattedText Format(double maxWidth)
    {
        var text = new FormattedText(Text ?? "", CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal), Math.Max(1, FontSize), Fill,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            LineHeight = Math.Max(1, FontSize * LineHeightFactor),
        };
        if (double.IsFinite(maxWidth) && maxWidth > 0) text.MaxTextWidth = maxWidth;
        return text;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var pad = Math.Max(0, StrokeThickness);
        var text = Format(availableSize.Width - 2 * pad);
        return new Size(Math.Ceiling(text.WidthIncludingTrailingWhitespace + 2 * pad), Math.Ceiling(text.Height + 2 * pad));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var pad = Math.Max(0, StrokeThickness);
        var width = Math.Max(1, RenderSize.Width - 2 * pad);
        var text = Format(width);
        // Ortalı yazı MaxTextWidth içinde ortalanır; MaxTextWidth hep verilir ki satırlar kutunun ortasında dursun.
        text.MaxTextWidth = width;
        var origin = new Point(pad, pad);
        if (pad <= 0)
        {
            drawingContext.DrawText(text, origin);
            return;
        }
        var geometry = text.BuildGeometry(origin);
        var pen = new Pen(Stroke, pad * 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawGeometry(null, pen, geometry);
        drawingContext.DrawGeometry(Fill, null, geometry);
    }
}
