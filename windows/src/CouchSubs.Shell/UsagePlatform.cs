// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Shell;

/// <summary>
/// Anonim sayımdaki platform (<c>p</c>): ps5 (Çevirgen PS5'e kendisi bağlanır), xbox (Xbox seçili), pc (bu bilgisayardaki
/// Remote Play ya da oyun penceresi), capture (OBS projektörü ya da capture card yazılımı). Seçilen pencerenin ne olduğu
/// anlaşılamazsa pc sayılır.
/// </summary>
public static class UsagePlatform
{
    public const string Ps5 = "ps5", Xbox = "xbox", Pc = "pc", Capture = "capture", Unknown = "unknown";

    public static string For(GameConsoleKind kind, UserSettings settings) => kind == GameConsoleKind.Xbox ? Xbox : settings.FrameSource switch
    {
        FrameSource.Ps5 => Ps5,
        FrameSource.RemotePlayWindow => Pc,
        FrameSource.CaptureWindow => IsCaptureSource(settings.CaptureWindow) ? Capture : Pc,
        _ => Unknown,
    };

    /// <summary>Görüntü kayıt/yayın yazılımlarının süreç adları (küçük harf, .exe'siz; başı eşleşir).</summary>
    private static readonly string[] CaptureProcesses =
    [
        "obs", "obs32", "obs64", "obs-studio", "streamlabs", "slobs", "xsplit", "4kcaptureutility", "gamecapture", "elgato",
        "recentral", "streamengine", "vmix", "amcap", "prismlivestudio",
    ];

    /// <summary>Seçilen pencere OBS, Streamlabs, XSplit, Elgato ya da AVerMedia gibi bir yakalama yazılımına mı ait.</summary>
    public static bool IsCaptureSource(WindowCaptureTarget? target)
    {
        if (target is null) return false;
        var process = target.ProcessName.Trim().ToLowerInvariant();
        if (process.EndsWith(".exe")) process = process[..^4];
        if (process.Length == 0) return false;
        // "obs" ile başlayan ama OBS olmayan yaygın uygulama (Obsidian not defteri) oyun penceresi sayılır.
        return CaptureProcesses.Any(process.StartsWith) && !process.StartsWith("obsidian");
    }
}
