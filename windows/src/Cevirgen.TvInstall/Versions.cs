// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.TvInstall;

public static class Versions
{
    /// <summary>Sayısal karşılaştırma: 0.10.0, 0.9.9'dan yenidir.</summary>
    public static bool IsNewer(string a, string b)
    {
        var x = Parts(a);
        var y = Parts(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var left = i < x.Length ? x[i] : 0;
            var right = i < y.Length ? y[i] : 0;
            if (left != right) return left > right;
        }
        return false;
    }

    private static int[] Parts(string version) =>
        [.. version.Split('.', '-').Select(p => int.TryParse(p, out var n) ? n : 0)];

    /// <summary>
    /// Çevirgen: klasörde aynı türden birden çok paket varsa (derleme çıktısında eski sürümün dosyası kalmış) en yeni
    /// sürümlüsü; sürümü okunabilen dosya yoksa null. Eskiden ilk bulunan alınıyordu, eski APK yenisinin önüne geçebiliyordu.
    /// </summary>
    public static string? Newest(IEnumerable<string> files)
    {
        string? best = null;
        string? bestVersion = null;
        foreach (var file in files)
        {
            if (OfBundledFile(file) is not { } version) continue;
            if (bestVersion is null || IsNewer(version, bestVersion)) (best, bestVersion) = (file, version);
        }
        return best;
    }

    /// <summary><c>cevirgen-androidtv_0.7.0.apk</c> → <c>0.7.0</c>; ad bu biçimde değilse null.</summary>
    public static string? OfBundledFile(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var underscore = name.LastIndexOf('_');
        return underscore > 0 && underscore < name.Length - 1 ? name[(underscore + 1)..] : null;
    }
}
