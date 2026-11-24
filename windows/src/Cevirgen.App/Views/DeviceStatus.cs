// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Cevirgen.App.Theme;
using Cevirgen.Core;
using Cevirgen.Shell;

namespace Cevirgen.App.Views;

/// <summary>Durum satırının bir öğesi ("● PS5-010"): ad, durum (renk) ve açıklama.</summary>
public sealed record DeviceItem(string Text, DeviceState State, string Hint);

/// <summary>
/// Çevirgen üst çubuğundaki kısa cihaz durumu ve Canlı ekranın boş hâlindeki liste: kaynak (PS5 adı / Xbox / pencere /
/// deneme), çıkış (TV adı / bu bilgisayar) ve çeviri motoru. Renkler tek modelden (<see cref="DeviceStates"/>): yeşil hazır,
/// sarı-turuncu bekleniyor, kırmızı hata, gri kapalı.
/// </summary>
public static class DeviceStatus
{
    public static IReadOnlyList<DeviceItem> Items(AppController controller)
    {
        var text = Localizer.Current;
        var items = new List<DeviceItem>();

        // Kaynak
        string source;
        if (controller.DemoMode) source = text["Demo Mode"];
        else if (controller.IsXbox ? controller.XboxReadsWindow : controller.Settings.FrameSource is FrameSource.CaptureWindow or FrameSource.RemotePlayWindow)
            source = controller.Settings.CaptureWindow?.Title is { Length: > 0 } title && controller.Settings.FrameSource == FrameSource.CaptureWindow
                ? Shorten(title) : controller.Settings.FrameSource == FrameSource.RemotePlayWindow && !controller.IsXbox ? "Remote Play" : "Pencere seçilmedi";
        else if (controller.IsXbox) source = controller.SelectedXboxConsole?.Name is { Length: > 0 } xbox ? xbox : "Xbox";
        else source = controller.Console?.Name is { Length: > 0 } name ? name : "PS5 kurulmadı";
        items.Add(new DeviceItem(source, controller.SourceState, controller.Ps5StatusText));

        // Çıkış
        if (!controller.ShowsOnTv) items.Add(new DeviceItem("Bu bilgisayar", controller.TvState, "Altyazı bu bilgisayarın ekranında"));
        else if (!controller.HasTv) items.Add(new DeviceItem("TV kurulmadı", controller.TvState, "TV eşleşmedi: Ayarlar › Cihazlar › TV"));
        else items.Add(new DeviceItem(controller.Tv?.Name is { Length: > 0 } tv ? Shorten(tv) : "TV", controller.TvState, controller.TvStatusText));

        // Çeviri motoru
        var engine = controller.EngineState;
        var hint = engine switch
        {
            DeviceState.Error => controller.OnlineIssue is { } issue
                ? issue.Describe(controller.Settings.OnlineEngine, text, controller.Settings.LocalAiServer)
                : "Ücretsiz çeviri bu bilgisayarda açılamadı. Çeviri sekmesinden başka bir motor seç.",
            DeviceState.Waiting when controller.Settings.Engine == TranslationEngine.Free => "Ücretsiz çeviri modeli henüz inmedi.",
            DeviceState.Waiting => "Bu motorun anahtarı eksik: Çeviri sekmesi.",
            _ => $"{controller.Settings.SourceLanguage.ToUpperInvariant()} → {controller.Settings.TargetLanguage.ToUpperInvariant()}",
        };
        items.Add(new DeviceItem(EngineName(controller.Settings), engine, hint));
        return items;
    }

    /// <summary>Motorun ve (yapay zekâda) modelin adı: "Gemini 2.5 Flash", "DeepL", "Ücretsiz çeviri".</summary>
    public static string EngineName(UserSettings settings)
    {
        var engine = settings.Engine;
        if (engine == TranslationEngine.Free) return "Ücretsiz çeviri";
        if (engine == TranslationEngine.Custom) return settings.CustomAiModel is { Length: > 0 } model ? Shorten(model) : BlindTest.EngineName(engine);
        if (engine.Provider() is { } provider && !provider.IsLocal())
            return Localizer.Current[provider.Model(settings.ModelFor(provider)).Label];
        return engine.DisplayName();
    }

    /// <summary>Durumun tema rengi (nokta): yeşil hazır, sarı-turuncu bekleniyor, kırmızı hata, gri kapalı.</summary>
    public static string Brush(DeviceState state) => state switch
    {
        DeviceState.Ready => "successBrush",
        DeviceState.Waiting => "warnBrush",
        DeviceState.Error => "liveBrush",
        _ => "faintBrush",
    };

    /// <summary>Durum noktası (7 px).</summary>
    public static Ellipse Dot(DeviceState state, double size = 7)
    {
        var dot = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("0", "0", "xs", "0") };
        dot.SetResourceReference(Shape.FillProperty, Brush(state));
        System.Windows.Automation.AutomationProperties.SetName(dot, state.Name());
        return dot;
    }

    /// <summary>Nokta + yazı (tek satır).</summary>
    public static StackPanel Line(DeviceState state, string text, string brush = "mutedBrush", double size = 13)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Dot(state));
        var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        row.Children.Add(label);
        return row;
    }

    private static string Shorten(string value) => value.Length <= 28 ? value : value[..27] + "…";
}
