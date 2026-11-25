// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core;

/// <summary>
/// Metin hattında bir satırın neden düşürüldüğü, bekletildiği ya da çevrilmediği: altyazı günlüğüne kısa, sınırlı bir
/// satır. Biçim: <c>düşen satır [Bileşen] neden: "metnin ilk 30 karakteri"</c>. Kullanıcı bildirimlerinde (GTA 6 telefon
/// mesajı bir kez çevrilip duruyor, görev satırı İngilizce kalıyor, periyodik kopmalar) hangi bileşenin satırı bıraktığı
/// günlükte görünsün diye. Gürültü yapmaz: aynı bileşen + neden + metin <see cref="RepeatMs"/> içinde yeniden yazılmaz;
/// bir dakikada en çok <see cref="PerMinute"/> satır yazılır, aşan satırlar sayılır ve sonraki yazılan satırın sonunda
/// bildirilir. Yalnız metin yazılır; ekran karesi ya da görüntü yazılmaz. Mac/iPhone (DropLog.swift) ve Android
/// (DropLog.kt) aynı biçim, aynı sınırlar.
/// </summary>
/// <param name="clock">Milisaniye saati; testlerde sahtesi verilir.</param>
/// <param name="sink">Varsayılanı altyazı günlüğü (<see cref="SubtitleLog"/>; açılmadıysa, ör. testlerde, hiçbir şey yazılmaz).</param>
public sealed class DropLog(Func<long>? clock = null, Action<string>? sink = null)
{
    /// <summary>Alıntının en uzun hâli (karakter).</summary>
    public const int ExcerptLength = 30;
    /// <summary>Aynı bileşen + neden + metin bu süre içinde yeniden yazılmaz.</summary>
    public const long RepeatMs = 60_000;
    /// <summary>Bir dakikada en çok yazılan satır.</summary>
    public const int PerMinute = 20;
    /// <summary>Hatırlanan en çok satır anahtarı (bellek sınırı).</summary>
    internal const int Memory = 256;

    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private readonly Action<string> sink = sink ?? SubtitleLog.Write;
    private readonly object gate = new();
    private readonly Dictionary<string, long> seen = [];
    private long? windowStart;
    private int written;
    private int suppressed;

    /// <summary>Yazılacak günlük satırı; aynı satır yakın zamanda yazıldıysa ya da dakikalık sınır dolduysa null.</summary>
    public string? Line(string component, string reason, string text)
    {
        var excerpt = SubtitleLog.Excerpt(text, ExcerptLength);
        var key = $"{component}|{reason}|{excerpt}";
        lock (gate)
        {
            var time = now();
            if (seen.TryGetValue(key, out var last) && time - last < RepeatMs) return null;
            if (windowStart is { } start && time - start < 60_000)
            {
                if (written >= PerMinute)
                {
                    suppressed++;
                    return null;
                }
            }
            else
            {
                windowStart = time;
                written = 0;
            }
            written++;
            seen[key] = time;
            if (seen.Count > Memory)
            {
                foreach (var old in seen.Where(pair => time - pair.Value >= RepeatMs).Select(pair => pair.Key).ToList()) seen.Remove(old);
                if (seen.Count > Memory)
                {
                    seen.Clear();
                    seen[key] = time;
                }
            }
            var line = $"düşen satır [{component}] {reason}: {excerpt}";
            if (suppressed > 0)
            {
                line += $" (+{suppressed} satır sınır yüzünden yazılmadı)";
                suppressed = 0;
            }
            return line;
        }
    }

    /// <summary>Satırı (yazılacaksa) <paramref name="log"/>'a, yoksa kurulurken verilen günlüğe yazar.</summary>
    public void Note(string component, string reason, string text, Action<string>? log = null)
    {
        if (Line(component, reason, text) is { } line) (log ?? sink)(line);
    }
}
