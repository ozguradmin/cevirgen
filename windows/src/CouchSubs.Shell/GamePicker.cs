// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;

namespace CouchSubs.Shell;

/// <summary>Karonun küçük etiketi: son oynanan ya da paketi kurulu.</summary>
public enum GameTileTag { None, Recent, Pack }

/// <summary>
/// "Hangi oyunu oynuyorsun?" seçicisinde bir karo: oyunun adı, kurulu paketin kimliği (varsa), kapak dosyası (yoksa null:
/// yazıyla karo), etiketi ve şu an oynanan oyun mu.
/// </summary>
public sealed record GameTile(string Name, string? PackId, string? CoverFile, GameTileTag Tag, bool Selected);

/// <summary>
/// Oyun seçicisinin mantığı (arayüzden bağımsız; sınanır). Sıra: son oynananlar (en yenisi başta), kurulu paketlerin oyunları,
/// sunucudaki oyunlar (<c>games</c>), popüler oyunlar; aynı oyun (<see cref="GameName.Key"/> ya da sunucudaki takma adlarla)
/// bir kez. Kapak: kullanıcının kendi kapağı (<see cref="GameCoverStore"/>, paket ya da oyun kimliğiyle) her zaman önce,
/// yoksa sunucudaki kapak, o da yoksa yazıyla karo.
/// </summary>
public static class GamePicker
{
    public const int MaxRecent = 8;
    /// <summary>Izgarada en çok bu kadar karo (arama yazılınca da).</summary>
    public const int MaxTiles = 48;

    /// <summary>Seçilen oyun son oynananların başına gelir; aynı oyun bir kez, en çok <see cref="MaxRecent"/>.</summary>
    public static IReadOnlyList<string> WithRecent(IEnumerable<string>? recent, string name)
    {
        var key = GameName.Key(name);
        if (key.Length == 0) return [.. (recent ?? [])];
        return [.. new[] { name.Trim() }.Concat((recent ?? []).Where(item => GameName.Key(item) != key)).Take(MaxRecent)];
    }

    /// <param name="query">Arama kutusundaki yazı (boşsa hepsi).</param>
    /// <param name="recent">Son oynananlar, en yenisi başta.</param>
    /// <param name="packs">Kurulu paketler (Ayarlar'daki raf; ek satır paketleri asıl paketle birleşmiş).</param>
    /// <param name="remote">Sunucudaki oyunlar ve inmiş kapakları.</param>
    /// <param name="chosen">Şu an oynanan oyun (karo seçili görünür).</param>
    /// <param name="userCover">Kimliğe göre kullanıcının kendi kapak dosyası (yoksa null).</param>
    public static IReadOnlyList<GameTile> Tiles(string? query, IReadOnlyList<string> recent, IReadOnlyList<PackShelfItem> packs,
                                                IReadOnlyList<ResolvedGame> remote, string? chosen, Func<string, string?> userCover,
                                                int limit = MaxTiles)
    {
        var playable = packs.Where(p => p.Id != GameSense.GeneralPack && !p.Title.EndsWith(GameCatalog.ExtraSuffix, StringComparison.Ordinal)).ToList();
        var candidates = new List<(string Name, bool Recent)>();
        candidates.AddRange(recent.Take(MaxRecent).Select(name => (name, true)));
        candidates.AddRange(playable.Select(pack => (pack.Title, false)));
        candidates.AddRange(remote.Select(game => (game.Game.Title, false)));
        candidates.AddRange(GameCatalog.Popular.Select(name => (name, false)));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<(GameTile Tile, IReadOnlyList<string> Keys)>();
        var chosenKey = GameName.Key(chosen);
        foreach (var (name, isRecent) in candidates)
        {
            var key = GameName.Key(name);
            if (key.Length == 0) continue;
            var pack = packs.FirstOrDefault(p => p.Titles.Append(p.Title).Any(title => GameName.Key(title) == key));
            var game = remote.FirstOrDefault(g => g.Game.Matches(name) || pack is not null && pack.PackIds.Contains(g.Game.Id));
            var keys = new List<string> { key };
            if (game is not null) keys.AddRange(new[] { game.Game.Title }.Concat(game.Game.Aliases).Select(GameName.Key).Where(k => k.Length > 0));
            if (keys.Any(seen.Contains)) continue;
            foreach (var k in keys) seen.Add(k);
            var cover = (pack is null ? null : userCover(pack.Id)) ?? (game is null ? null : userCover(game.Game.Id)) ?? game?.CoverFile;
            var tag = isRecent ? GameTileTag.Recent : pack is not null ? GameTileTag.Pack : GameTileTag.None;
            all.Add((new GameTile(name.Trim(), pack?.Id, cover, tag, chosenKey.Length > 0 && keys.Contains(chosenKey)), keys.Distinct().ToList()));
        }

        var wanted = GameName.Key(query);
        if (wanted.Length == 0) return [.. all.Take(limit).Select(item => item.Tile)];
        var found = all.Where(item => item.Keys.Any(k => k.Contains(wanted, StringComparison.Ordinal))).ToList();
        return [.. found.Where(item => item.Keys.Any(k => k.StartsWith(wanted, StringComparison.Ordinal)))
            .Concat(found.Where(item => !item.Keys.Any(k => k.StartsWith(wanted, StringComparison.Ordinal))))
            .Take(limit).Select(item => item.Tile)];
    }

    /// <summary>
    /// Arama kutusunda Enter: yazı bir karonun adına ya da takma adına tam denkse o oyun, yoksa tek sonuç varsa o, yoksa
    /// yazılan ad olduğu gibi ("Listede yok"). Boş yazıda null.
    /// </summary>
    public static string? EnterChoice(string? query, IReadOnlyList<GameTile> tiles, IReadOnlyList<ResolvedGame> remote)
    {
        var typed = query?.Trim() ?? "";
        var key = GameName.Key(typed);
        if (key.Length == 0) return null;
        var exact = tiles.FirstOrDefault(tile => GameName.Key(tile.Name) == key
            || remote.Any(game => game.Game.Matches(tile.Name) && game.Game.Matches(typed)));
        if (exact is not null) return exact.Name;
        return tiles.Count == 1 ? tiles[0].Name : typed;
    }

    /// <summary>Karonun etiketi: "Son oynanan", "Paket var" ya da yok.</summary>
    public static string? TagText(GameTileTag tag) => tag switch
    {
        GameTileTag.Recent => "Son oynanan",
        GameTileTag.Pack => "Paket var",
        _ => null,
    };
}
