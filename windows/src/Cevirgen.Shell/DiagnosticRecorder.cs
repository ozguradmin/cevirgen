// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Çevirgen: tanılama kaydı (Ayarlar › Gelişmiş › "Tanılama kaydı"; canlı ayar için). Açıkken ve çeviri sürerken beş saniyede
/// bir o anki kare JPEG olarak (en çok 1280 px genişlik) ve yanında küçük bir JSON (alanlar yüzde olarak, son yazı tanıma
/// satırları kutularıyla, son gösterilen çeviri) %LOCALAPPDATA%\Cevirgen\tanilama\&lt;oturum zamanı&gt;\ altına yazılır.
/// Oturum başına en çok <see cref="MaxFrames"/> kare; üç günden eski oturum klasörleri silinir. 30 dakika sonra ya da
/// uygulama yeniden açılınca kendiliğinden kapanır (ayar diske yazılmaz). Hiçbir yere gönderilmez.
/// </summary>
public sealed class DiagnosticRecorder
{
    public const string FolderName = "tanilama";
    public const int MaxFrames = 400;
    public const int ImageWidth = 1280;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AutoOff = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan KeepSessions = TimeSpan.FromDays(3);
    private const string SessionFormat = "yyyy-MM-dd_HH-mm-ss";

    private readonly object gate = new();
    private readonly Func<Frame, int, byte[]?> encode;
    private readonly Func<DateTime> clock;
    private DateTime? lastShot;
    private int busy;

    /// <param name="root">Tanılama klasörü (%LOCALAPPDATA%\Cevirgen\tanilama).</param>
    /// <param name="encode">Kareyi en çok verilen genişlikte JPEG'e çevirir; çeviremezse null.</param>
    /// <param name="clock">Yerel saat (testlerde sahte).</param>
    public DiagnosticRecorder(string root, Func<Frame, int, byte[]?> encode, Func<DateTime>? clock = null)
    {
        Root = root;
        this.encode = encode;
        this.clock = clock ?? (() => DateTime.Now);
    }

    public string Root { get; }
    public bool Enabled { get { lock (gate) return StartedAt is not null; } }
    public DateTime? StartedAt { get; private set; }
    /// <summary>Bu oturumun klasörü; kapalıyken null.</summary>
    public string? SessionFolder { get; private set; }
    public int Frames { get; private set; }

    /// <summary>Kaydı açar: yeni oturum klasörü (yerel saat, "2026-11-24_21-05-09") ve eski oturumların temizliği.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (StartedAt is not null) return;
            var now = clock();
            StartedAt = now;
            SessionFolder = Path.Combine(Root, now.ToString(SessionFormat, CultureInfo.InvariantCulture));
            Frames = 0;
            lastShot = null;
        }
        CleanOld();
    }

    public void Stop()
    {
        lock (gate)
        {
            StartedAt = null;
            SessionFolder = null;
            lastShot = null;
        }
    }

    /// <summary>30 dakika doldu mu (açıkken).</summary>
    public bool Expired
    {
        get { lock (gate) return StartedAt is { } started && clock() - started >= AutoOff; }
    }

    /// <summary>Şimdi kare yazılmalı mı: açık, sınır dolmadı, son kareden beri beş saniye geçti ve önceki yazım bitti.</summary>
    public bool Due()
    {
        lock (gate)
            return StartedAt is not null && Frames < MaxFrames && Volatile.Read(ref busy) == 0
                   && (lastShot is not { } last || clock() - last >= Interval);
    }

    /// <summary>
    /// Kareyi ve yanındaki JSON'u yazar (çağıran iş parçacığında; uygulama arka planda çağırır). Kapalıysa, sınır dolduysa ya
    /// da sıra gelmediyse false. Yazma hatası yalnız günlüğe gider.
    /// </summary>
    public bool Record(Frame frame, DiagnosticSnapshot snapshot)
    {
        string folder;
        int number;
        lock (gate)
        {
            if (!Due() || SessionFolder is null || frame.Width <= 0 || frame.Height <= 0) return false;
            folder = SessionFolder;
            number = ++Frames;
            lastShot = clock();
        }
        if (Interlocked.Exchange(ref busy, 1) == 1) return false;
        try
        {
            var image = encode(frame, ImageWidth);
            if (image is null) return false;
            Directory.CreateDirectory(folder);
            var name = number.ToString("0000", CultureInfo.InvariantCulture);
            File.WriteAllBytes(Path.Combine(folder, name + ".jpg"), image);
            var savedWidth = Math.Min(frame.Width, ImageWidth);
            var sidecar = Sidecar(snapshot, name + ".jpg", frame.Width, frame.Height, savedWidth,
                (int)Math.Round(frame.Height * (double)savedWidth / frame.Width));
            File.WriteAllText(Path.Combine(folder, name + ".json"), sidecar);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            SubtitleLog.Write($"tanılama karesi yazılamadı: {error.GetType().Name}");
            return false;
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }
    }

    /// <summary>Üç günden eski oturum klasörleri silinir (adı tarih biçiminde olmayanlara dokunulmaz).</summary>
    public void CleanOld()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            var limit = clock() - KeepSessions;
            foreach (var directory in Directory.GetDirectories(Root))
            {
                if (!DateTime.TryParseExact(Path.GetFileName(directory), SessionFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
                if (at >= limit) continue;
                try { Directory.Delete(directory, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Kare yanındaki JSON (alanlar ve kutular karenin yüzdesi olarak, 0–100; üstten ve soldan).</summary>
    internal static string Sidecar(DiagnosticSnapshot snapshot, string image, int width, int height, int savedWidth, int savedHeight)
    {
        static object Percent(double left, double top, double right, double bottom) => new
        {
            left = Math.Round(left * 100, 2), top = Math.Round(top * 100, 2), right = Math.Round(right * 100, 2), bottom = Math.Round(bottom * 100, 2),
        };
        var region = snapshot.Subtitle;
        var area = new TextBox(region.Left, region.Top, region.Right, region.Bottom);
        object? ocr = null;
        if (snapshot.Ocr is { } read)
        {
            var place = new TextBox(read.Region.Left, read.Region.Top, read.Region.Right, read.Region.Bottom);
            ocr = new
            {
                area = Percent(place.Left, place.Top, place.Right, place.Bottom),
                lines = read.Reading.Lines.Select(line => new
                {
                    text = line.Text,
                    box = line.Box is { } box ? Percent(box.PlacedIn(place).Left, box.PlacedIn(place).Top, box.PlacedIn(place).Right, box.PlacedIn(place).Bottom) : null,
                    confidence = line.Confidence is { } c ? Math.Round(c, 3) : (double?)null,
                }).ToList(),
            };
        }
        var body = new
        {
            at = snapshot.At.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            image,
            frame = new { width, height, savedWidth, savedHeight },
            game = snapshot.Game,
            areas = new
            {
                subtitle = Percent(area.Left, area.Top, area.Right, area.Bottom),
                extra = snapshot.Extra.Select(extra => new
                {
                    id = extra.Id, name = extra.Name, auto = extra.Auto ? true : (bool?)null,
                    region = Percent(extra.Region.Left, extra.Region.Top, extra.Region.Right, extra.Region.Bottom),
                }).ToList(),
            },
            ocr,
            lastShown = snapshot.LastSource is null && snapshot.LastTranslation is null ? null
                : new { source = snapshot.LastSource, translation = snapshot.LastTranslation },
        };
        return JsonSerializer.Serialize(body, JsonOptions);
    }
}

/// <summary>Tanılama karesinin yanına yazılanlar (o anki durum).</summary>
/// <param name="Ocr">Altyazı hattının son okuması ve okunan alan; hiç okunmadıysa null.</param>
public sealed record DiagnosticSnapshot(DateTimeOffset At, CropRegion Subtitle, IReadOnlyList<WatchedArea> Extra,
    (Reading Reading, CropRegion Region)? Ocr, string? LastSource, string? LastTranslation, string? Game);
