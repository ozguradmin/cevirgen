// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>Durum satırındaki bir öğenin rengi: yeşil bağlı/hazır, turuncu sorun, gri kurulmamış ya da bekliyor.</summary>
public enum DeviceTone { Ok, Warn, Idle }

/// <summary>Durum satırının bir öğesi ("● PS5-010").</summary>
public sealed record DeviceItem(string Text, DeviceTone Tone, string Hint);

/// <summary>
/// Çevirgen üst çubuğundaki kısa cihaz durumu (taslak: Main.dc.html › üst çubuk) ve Canlı ekranın boş hâlindeki "bağlı
/// olanlar" listesi: kaynak (PS5 adı / Xbox / pencere / deneme), çıkış (TV adı / bu bilgisayar) ve çeviri motoru.
/// </summary>
public static class DeviceStatus
{
    public static IReadOnlyList<DeviceItem> Items(AppController controller)
    {
        var text = Localizer.Current;
        var running = controller.IsRunning;
        var items = new List<DeviceItem>();

        // Kaynak
        var sourceTone = controller.DemoMode ? DeviceTone.Ok
            : running ? controller.Ps5Tone switch { StatusTone.Good => DeviceTone.Ok, StatusTone.Warning => DeviceTone.Warn, _ => DeviceTone.Idle }
            : controller.CanStart ? DeviceTone.Ok : DeviceTone.Idle;
        string source;
        if (controller.DemoMode) source = text["Demo Mode"];
        else if (controller.IsXbox ? controller.XboxReadsWindow : controller.Settings.FrameSource is FrameSource.CaptureWindow or FrameSource.RemotePlayWindow)
            source = controller.Settings.CaptureWindow?.Title is { Length: > 0 } title && controller.Settings.FrameSource == FrameSource.CaptureWindow
                ? Shorten(title) : controller.Settings.FrameSource == FrameSource.RemotePlayWindow && !controller.IsXbox ? "Remote Play" : "Pencere seçilmedi";
        else if (controller.IsXbox) source = controller.SelectedXboxConsole?.Name is { Length: > 0 } xbox ? xbox : "Xbox";
        else source = controller.Console?.Name is { Length: > 0 } name ? name : "PS5 kurulmadı";
        items.Add(new DeviceItem(source, sourceTone, controller.Ps5StatusText));

        // Çıkış
        if (!controller.ShowsOnTv) items.Add(new DeviceItem("Bu bilgisayar", DeviceTone.Ok, text["Computer Screen"]));
        else if (!controller.HasTv) items.Add(new DeviceItem("TV kurulmadı", DeviceTone.Idle, controller.TvStatusText));
        else
        {
            var tone = controller.TvTone == StatusTone.Warning ? DeviceTone.Warn : controller.TvClients > 0 || !running ? DeviceTone.Ok : DeviceTone.Idle;
            items.Add(new DeviceItem(controller.Tv?.Name is { Length: > 0 } tv ? Shorten(tv) : "TV", tone, controller.TvStatusText));
        }

        // Çeviri motoru
        var failing = controller.TranslatorStatus == TranslatorStatus.EngineFailed || (running && controller.OnlineIssue is not null);
        items.Add(new DeviceItem(EngineName(controller.Settings), failing ? DeviceTone.Warn : DeviceTone.Ok,
            $"{controller.Settings.SourceLanguage.ToUpperInvariant()} → {controller.Settings.TargetLanguage.ToUpperInvariant()}"));
        return items;
    }

    /// <summary>Motorun ve (yapay zekâda) modelin adı: "Gemini 2.5 Flash", "DeepL", "Ücretsiz çeviri".</summary>
    public static string EngineName(UserSettings settings)
    {
        var engine = settings.Engine;
        if (engine == TranslationEngine.Free) return "Ücretsiz çeviri";
        if (engine.Provider() is { } provider && !provider.IsLocal())
            return Localizer.Current[provider.Model(settings.ModelFor(provider)).Label];
        return engine.DisplayName();
    }

    private static string Shorten(string value) => value.Length <= 28 ? value : value[..27] + "…";
}
