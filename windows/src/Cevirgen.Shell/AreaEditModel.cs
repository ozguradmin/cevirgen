// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>Alanları düzenle ekranındaki bir kutu: altyazı alanı (<see cref="Main"/>) ya da ek alan.</summary>
/// <param name="Live">Ek alanda "Sürekli" (çeviri sürerken saniyede bir okunur); kapalıysa "Kısayolla" (tuşa basınca bir kez).</param>
/// <param name="Target">Kısayolla okununca sonucun gösterileceği yer.</param>
public sealed record AreaBox(string Id, string Name, CropRegion Region, bool Main, bool Live = true, ResultTarget Target = ResultTarget.Tv);

/// <summary>
/// Çevirgen: Alanları düzenle ekranının modeli (taslak: Areas.dc.html). Altyazı alanı ve ek alanlar kutu olarak düzenlenir:
/// fareyle taşınır ve köşe/kenardan boyutlanır (<see cref="RegionEdit"/> kuralları; altyazı alanında "Ortada tut" açıksa
/// yatay boyut ortadan değişir), seçili kutu ok tuşlarıyla %0,5, Shift ile %5 kayar (Ctrl ile sağ/alt kenar büyür ya da
/// küçülür). Ek alan eklenir, silinir, adı ve "Sürekli / Kısayolla" seçimi değişir; "Varsayılana dön" alt bölgeye ve ek
/// alansız hâle döner; "Oyundan otomatik bul" oyunun hazır alanlarını (<see cref="GameAreaPresets"/>) ekler. "Bitti"
/// <see cref="ToAreaSet"/>'i kaydeder. Arayüzden bağımsızdır; test edilir.
/// </summary>
public sealed class AreaEditModel
{
    /// <summary>Ok tuşuyla bir adım (kenara oranla) ve Shift ile büyük adım.</summary>
    public const double SmallStep = 0.005;
    public const double LargeStep = 0.05;
    /// <summary>Altyazı alanının kimliği (ek alanların kimliği GUID'dir).</summary>
    public const string MainId = "main";

    private readonly List<AreaBox> boxes = [];
    private readonly bool keepCentered;

    public AreaEditModel(AreaSet areas, bool keepCentered = true)
    {
        this.keepCentered = keepCentered;
        Load(areas);
        Changed = false;
    }

    /// <summary>Kutular: önce altyazı alanı, sonra ek alanlar (kayıt sırasıyla).</summary>
    public IReadOnlyList<AreaBox> Boxes => boxes;

    /// <summary>Seçili kutunun kimliği (başta altyazı alanı).</summary>
    public string Selected { get; private set; } = MainId;

    /// <summary>Kaydedilmemiş değişiklik var.</summary>
    public bool Changed { get; private set; }

    public AreaBox? SelectedBox => boxes.FirstOrDefault(box => box.Id == Selected);

    public bool CanAdd => boxes.Count - 1 < ExtraArea.MaxCount;

    private void Load(AreaSet areas)
    {
        boxes.Clear();
        boxes.Add(new AreaBox(MainId, "Altyazı", areas.Region, Main: true));
        foreach (var area in areas.ExtraAreas)
            boxes.Add(new AreaBox(area.Id, area.Name, area.Region, Main: false, area.Enabled, area.Target));
        if (boxes.All(box => box.Id != Selected)) Selected = MainId;
    }

    public void Select(string id)
    {
        if (boxes.Any(box => box.Id == id)) Selected = id;
    }

    /// <summary>Kutunun ekranda gösterilen adı: "Altyazı", ek alanda verilen ad ya da "Kutu 2".</summary>
    public string DisplayName(AreaBox box) => box.Main ? "Altyazı" : string.IsNullOrWhiteSpace(box.Name) ? $"Kutu {Number(box)}" : box.Name.Trim();

    /// <summary>Kutunun numarası (altyazı 1, ek alanlar 2'den).</summary>
    public int Number(AreaBox box) => boxes.FindIndex(item => item.Id == box.Id) + 1;

    /// <summary>"Kısayolla" okunan ek alanın kısayol sırası (1–3); kısayolu yoksa null.</summary>
    public int? ShortcutSlot(AreaBox box)
    {
        if (box.Main) return null;
        var index = boxes.FindIndex(item => item.Id == box.Id) - 1;
        return index >= 0 && index < ExtraArea.ShortcutCount ? index + 1 : null;
    }

    /// <summary>Kutu sürüklendi: <paramref name="start"/> bölgesinden (dx, dy) kadar; <paramref name="handle"/> None ise taşır.</summary>
    public void Drag(string id, CropRegion start, RegionEdit.Handle handle, double dx, double dy) =>
        Update(id, box => box with { Region = RegionEdit.Dragged(start, handle, dx, dy, box.Main && keepCentered) });

    /// <summary>
    /// Ok tuşu: seçili kutu (dx, dy) yönde bir adım kayar (<paramref name="large"/>: Shift ile %5). <paramref name="resize"/>
    /// (Ctrl): sağ ve alt kenar oynar, kutu büyür ya da küçülür.
    /// </summary>
    public void Nudge(int dx, int dy, bool large, bool resize = false)
    {
        if (SelectedBox is not { } box) return;
        var step = large ? LargeStep : SmallStep;
        var handle = resize ? (dx != 0 ? RegionEdit.Handle.Right : RegionEdit.Handle.None) | (dy != 0 ? RegionEdit.Handle.Bottom : RegionEdit.Handle.None)
            : RegionEdit.Handle.None;
        if (resize && handle == RegionEdit.Handle.None) return;
        // Ortada tutulan altyazı alanında sağ kenar büyüyünce sol kenar da açılır (fareyle olduğu gibi).
        Update(box.Id, current => current with { Region = RegionEdit.Dragged(current.Region, handle, dx * step, dy * step, current.Main && keepCentered) });
    }

    /// <summary>Yeni ek alan ("Kutu N"), ekranın ortasında; seçili olur. Sınıra gelindiyse null.</summary>
    public AreaBox? Add()
    {
        if (!CanAdd) return null;
        var box = new AreaBox(Guid.NewGuid().ToString(), "", new CropRegion(0.40, 0.55, 0.30, 0.70), Main: false);
        boxes.Add(box);
        Selected = box.Id;
        Changed = true;
        return box;
    }

    /// <summary>Ek alan silinir (altyazı alanı silinmez; onun yerine "Varsayılana dön").</summary>
    public bool Remove(string id)
    {
        var index = boxes.FindIndex(box => box.Id == id && !box.Main);
        if (index < 0) return false;
        boxes.RemoveAt(index);
        if (Selected == id) Selected = boxes[Math.Min(index, boxes.Count - 1)].Id;
        Changed = true;
        return true;
    }

    public void Rename(string id, string name) => Update(id, box => box.Main ? box : box with { Name = name.Trim() });

    public void SetLive(string id, bool live) => Update(id, box => box.Main ? box : box with { Live = live });

    public void SetTarget(string id, ResultTarget target) => Update(id, box => box.Main ? box : box with { Target = target });

    /// <summary>Varsayılan alanlar: alttaki altyazı alanı, ek alan yok.</summary>
    public void ResetToDefaults()
    {
        Load(new AreaSet(AreaPreset.Bottom.Region()!, AreaPreset.Bottom, []));
        Selected = MainId;
        Changed = true;
    }

    /// <summary>Bu oyunun hazır alanları var mı ("Oyundan otomatik bul" yalnız o zaman görünür).</summary>
    public static bool CanAutoFind(string? game) => GameAreaPresets.Areas(game).Count > 0;

    /// <summary>
    /// Oyunun hazır alanları ek alan olarak eklenir (sürekli okunur); aynı kimlikli alan zaten varsa yeniden eklenmez.
    /// Eklenen alan sayısını döner.
    /// </summary>
    public int AutoFind(string? game)
    {
        var added = 0;
        foreach (var preset in GameAreaPresets.Areas(game))
        {
            if (!CanAdd || boxes.Any(box => box.Id == preset.Id)) continue;
            boxes.Add(new AreaBox(preset.Id, PresetName(preset.Name), preset.Region, Main: false));
            added++;
        }
        if (added > 0)
        {
            Selected = boxes[^1].Id;
            Changed = true;
        }
        return added;
    }

    private static string PresetName(string name) => name switch
    {
        "Help" => "İpucu",
        "Messages" => "Mesajlar",
        _ => name,
    };

    /// <summary>Kaydedilecek alanlar (altyazı alanının hazır seçimi bölgesinden çıkarılır).</summary>
    public AreaSet ToAreaSet()
    {
        var main = boxes[0].Region;
        return new AreaSet(main, AreaPresets.Matching(main),
            [.. boxes.Skip(1).Select(box => new ExtraArea(box.Id, box.Name, box.Region, box.Live, box.Target))]);
    }

    /// <summary>Koordinatlar yüzde olarak (mono): "x 14 % · y 64 % · 72 × 33 %"; ondalık virgülle ("2,5").</summary>
    public static string Coordinates(CropRegion region)
    {
        static string P(double value) => Math.Round(value * 100, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.GetCultureInfo("tr-TR"));
        return $"x {P(region.Left)} % · y {P(region.Top)} % · {P(region.Right - region.Left)} × {P(region.Bottom - region.Top)} %";
    }

    private void Update(string id, Func<AreaBox, AreaBox> change)
    {
        var index = boxes.FindIndex(box => box.Id == id);
        if (index < 0) return;
        var next = change(boxes[index]);
        if (next == boxes[index]) return;
        boxes[index] = next;
        Changed = true;
    }
}
