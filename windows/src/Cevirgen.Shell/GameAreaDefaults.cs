// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Çevirgen: bilinen oyunların hazır alan profili. Oyun ilk kez seçildiğinde (o oyunun kayıtlı profili yoksa) bu alanlar
/// profil olarak kaydedilir ve uygulanır; kullanıcı "Alanları düzenle"den değiştirir, değişiklik kendi profiline yazılır.
/// Değerler 2026-11-24'te PS5 akışında gerçek GTA 6 ekranında çizildi (altyazı, sol üst ipucu kutusu, sağ alt tuş listesi).
/// </summary>
public static class GameAreaDefaults
{
    /// <summary>GTA 6 sol üstteki ipucu kutusu.</summary>
    public const string Gta6HelpId = "gta6-ipucu";
    /// <summary>GTA 6 sağ alttaki tuş/seçenek listesi (TV, dolap, mağaza menüleri).</summary>
    public const string Gta6OptionsId = "gta6-secenekler";

    private static readonly Dictionary<string, AreaSet> Table = new()
    {
        ["gta 6"] = new AreaSet(
            new CropRegion(Top: 0.69, Bottom: 0.995, Left: 0.14, Right: 0.86),
            AreaPreset.Custom,
            [
                new ExtraArea(Gta6HelpId, "İpucu", new CropRegion(Top: 0.25, Bottom: 0.40, Left: 0.0125, Right: 0.2365)),
                // Sol kenar geniş: 0,88'de "DEFAULT" "ULT", "SELECT" "ECT" okunuyor, telefon bildirimi yarım kalıyordu.
                new ExtraArea(Gta6OptionsId, "Seçenekler", new CropRegion(Top: 0.66, Bottom: 0.993, Left: 0.75, Right: 1.0)),
            ]),
    };

    /// <summary>
    /// Oyunun kendi altyazısını açma yolu (oyunun menüsü İngilizce olduğundan menü adları İngilizce); bilinmiyorsa null.
    /// GTA 6'da altyazı varsayılan olarak kapalı geliyor.
    /// </summary>
    public static string? SubtitleSettingHint(string? game) =>
        game is not null && GameName.Key(game) == "gta 6"
            ? "GTA 6'da altyazı varsayılan olarak kapalı gelir: oyunda Settings › UI › Subtitles & Captions › Subtitles'ı On yap."
            : null;

    /// <summary>Oyunun hazır alanları; tabloda yoksa null.</summary>
    public static AreaSet? For(string? game) =>
        game is null ? null : Table.TryGetValue(GameName.Key(game), out var areas) ? areas : null;
}
