// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;

namespace CouchSubs.Core;

/// <summary>
/// Çevirgen: geçmişten SRT altyazı dosyası. Her satır bir ipucu (cue); zamanlar satırın gerçekten ekrana geldiği ve
/// kalktığı anlardır (<see cref="SubtitleHistory.Entry.ShownAt"/>, <see cref="SubtitleHistory.Entry.LeftAt"/>), oturumun
/// başına göre, standart <c>SS:DD:ss,mmm --&gt; SS:DD:ss,mmm</c> biçiminde. Kalkışı bilinmeyen satır okunacak kadar
/// (<see cref="FallbackMs"/>) kalır; hiçbir ipucu sonrakinin başlangıcını geçmez. Satırlar CRLF ile ayrılır (eski
/// oynatıcılar da okur); dosya UTF-8'dir.
/// </summary>
public static class SrtExport
{
    public enum Kind
    {
        /// <summary>Yalnız Türkçe.</summary>
        Turkish,
        /// <summary>İki dilli: her ipucunda önce İngilizce, sonra Türkçe.</summary>
        Bilingual,
    }

    /// <summary>Bir ipucu en az bu kadar sürer (sıfır süreli ipucunu bazı oynatıcılar atar).</summary>
    public const int MinCueMs = 100;

    /// <summary>Kalkışı bilinmeyen satırın süresi: metnin uzunluğuna göre (saniyede 17 karakter), 1–7 sn.</summary>
    public static int FallbackMs(string text) =>
        (int)Math.Clamp(ReadingSpeed.Characters(text) / ReadingSpeed.MaxCps * 1000, 1000, 7000);

    /// <param name="entries">Geçmiş kayıtları (sıra önemli değil; ekrana geliş sırasıyla yazılır).</param>
    /// <param name="origin">Oturumun başladığı an (UTC); verilmezse ilk satırın ekrana geldiği an.</param>
    public static string Build(IEnumerable<SubtitleHistory.Entry> entries, Kind kind, DateTime? origin = null)
    {
        var list = entries.Where(entry => entry.Translation.Trim().Length > 0)
            .OrderBy(entry => entry.Shown).ThenBy(entry => entry.Id).ToList();
        if (list.Count == 0) return "";
        var start = (origin ?? list[0].Shown).ToUniversalTime();
        long Offset(DateTime at) => (long)Math.Round((at.ToUniversalTime() - start).TotalMilliseconds);
        var builder = new StringBuilder();
        for (var i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            var from = Math.Max(0, Offset(entry.Shown));
            var to = entry.LeftAt is { } left ? Offset(left) : from + FallbackMs(entry.Translation);
            if (i + 1 < list.Count) to = Math.Min(to, Math.Max(0, Offset(list[i + 1].Shown)));
            to = Math.Max(to, from + MinCueMs);
            builder.Append(i + 1).Append("\r\n");
            builder.Append(Time(from)).Append(" --> ").Append(Time(to)).Append("\r\n");
            if (kind == Kind.Bilingual) builder.Append(Lines(entry.Source)).Append("\r\n");
            builder.Append(Lines(entry.Translation)).Append("\r\n\r\n");
        }
        return builder.ToString();
    }

    /// <summary>
    /// Dosyaya yazar (UTF-8, BOM'lu: Windows oynatıcıları Türkçe harfleri doğru okusun); yazılan ipucu sayısını döner.
    /// Yazılamazsa <see cref="IOException"/> ya da <see cref="UnauthorizedAccessException"/> atar.
    /// </summary>
    public static int Write(string path, IEnumerable<SubtitleHistory.Entry> entries, Kind kind, DateTime? origin = null)
    {
        var list = entries.Where(entry => entry.Translation.Trim().Length > 0).ToList();
        var text = Build(list, kind, origin);
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        File.WriteAllText(path, text, new UTF8Encoding(true));
        return list.Count;
    }

    /// <summary>"01:02:03,045"; saat iki haneyi aşabilir (100 saatten uzun oturum).</summary>
    public static string Time(long ms)
    {
        ms = Math.Max(0, ms);
        var hours = ms / 3_600_000;
        var minutes = ms / 60_000 % 60;
        var seconds = ms / 1000 % 60;
        var millis = ms % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00},{millis:000}");
    }

    /// <summary>Metnin satırları CRLF ile; boş satır ipucunu bitirmesin diye atılır.</summary>
    private static string Lines(string text) =>
        string.Join("\r\n", text.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
}
