// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace Cevirgen.Core;

internal static partial class GamePhoneText
{
    // Hesap ve etiket satırı zaten telefonun üstünde okunabilir. Çeviri kutusu onu örtmesin.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])[@#][\p{L}\p{N}_]+(?:[.-][\p{L}\p{N}_]+)*")]
    private static partial Regex Identifier();

    // Aynı uzun mesajda yan yana iki kısa sözcükte birer harf oynamış ("it out" → "ct ocut").
    // Tek kelimelik seçenekler, eklenen/silinen sözcükler, sayılar ve olumsuzluklar doğrudan işlenir.
    internal static bool HasAdjacentOcrEdits(string shown, string next)
    {
        var before = TextMatch.Normalize(shown).Split(' ');
        var after = TextMatch.Normalize(next).Split(' ');
        if (before.Length < 9 || before.Length != after.Length) return false;
        int first = 0, last = before.Length - 1;
        while (first <= last && before[first] == after[first]) first++;
        while (last > first && before[last] == after[last]) last--;
        if (last != first + 1) return false;
        for (int i = first; i <= last; i++)
        {
            var a = before[i];
            var b = after[i];
            int length = Math.Max(a.Length, b.Length);
            if (Math.Min(a.Length, b.Length) < 2 || length > 6 || !a.All(char.IsLetter) || !b.All(char.IsLetter)
                || !TextMatch.IsSimilar(a, b, 1.0 - 1.0 / length)) return false;
        }
        return !TextMatch.AddsMeaning(next, shown) && !TextMatch.AddsMeaning(shown, next);
    }

    internal static bool ContainsIdentifier(string text) =>
        (text.Contains('@') || text.Contains('#')) && Identifier().IsMatch(text);

    // Sayıyla başlayan gerçek mesaj sözcüklerini altyazı gürültü filtresinden geçir.
    // @hesap, etiket, ürün ve yer kodlarının içini değiştirme.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_@#.-])(?:2gether|4u)(?![\p{L}\p{N}_])")]
    private static partial Regex ChatWord();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_@#])(?:IO|I0|lO|l0)(?=\s+min(?:s|utes?)?\b)")]
    private static partial Regex MisreadTenMinutes();

    internal static bool IsWrittenChatWord(string token) => (token.Contains('2') || token.Contains('4')) && ChatWord().IsMatch(token);

    internal static string EnglishModelInput(string text)
    {
        // Açık bir süre birimi yoksa I/1 veya O/0 hakkında karar verme.
        if (text.Contains("min", StringComparison.Ordinal)) text = MisreadTenMinutes().Replace(text, "10");
        return !IsWrittenChatWord(text) ? text :
            ChatWord().Replace(text, word => word.Value == "2gether" ? "together" : "for you");
    }

    private static bool IsTag(string word) => word.Length > 1 && word[0] is '@' or '#'
        && Identifier().Match(word) is { Success: true, Index: 0 } token && token.Length == word.Length;

    internal static string? KeepIdentifiers(string source, string? translation)
    {
        if (string.IsNullOrEmpty(translation) || !ContainsIdentifier(source)) return translation;
        var names = Identifier().Matches(source).Select(m => m.Value).ToList();
        bool unexpected = false;
        var restored = Identifier().Replace(translation, match =>
        {
            int i = names.FindIndex(n => string.Equals(n, match.Value, StringComparison.OrdinalIgnoreCase));
            if (i < 0) { unexpected = true; return match.Value; }
            string original = names[i];
            names.RemoveAt(i);
            return original;
        });
        // Yanlış kişiye yönlendiren çeviriyi göstermemek için bu bloğun aslı ekranda kalır.
        return unexpected || names.Count != 0 ? null : restored;
    }

    /// <summary>
    /// Çeviriye gidecek satırlar. Sohbet listesi (Mesajlar ekranı: "Suggested", "Messages", kişi adı ve altında son mesajın
    /// önizlemesi) tanınırsa her satır rolüyle işaretlenir (<see cref="TextLine.ListRole"/>): kişi adı (çevrilmez, kalın),
    /// önizleme (altında, çevrilir), bölüm başlığı. Liste için kişi adı sütununda en az üç "ad + önizleme" çifti ve bir bölüm
    /// başlığı (ya da başlık okunamadıysa "…" ile kesilmiş en az iki önizleme) gerekir. Önceden iki başlığın ikisi de
    /// okunmalıydı: sallanan telefonda biri her okumada kaçıyor, liste bir sohbet balonları bir düz satırlar olarak gidip
    /// geliyordu (LG, beta.93). Kişi adları da atılıyordu; şimdi listede adıyla görünür. Mac ile aynı.
    /// </summary>
    internal static Reading ForTranslation(Reading reading)
    {
        var lines = new List<TextLine>();
        foreach (var line in reading.Lines)
        {
            var words = line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(IsTag) && words.All(w => IsTag(w) || !w.Any(char.IsLetterOrDigit))) continue;
            lines.Add(line);
        }

        // Kişi adını tek başına büyük harften çıkarmak "May" gibi kısa gerçek mesajları kaybettirir.
        // Yalnız aynı sütunda tekrarlanan ad/önizleme düzeni ve bir başlık (ya da kesik önizlemeler) varsa liste sayılır.
        lines = lines.Select(l => SectionLabel(l.Text) is { } label ? l with { Text = label } : l).ToList();
        var headed = lines.Any(l => SectionLabel(l.Text) is not null);
        var order = Enumerable.Range(0, lines.Count).Where(i => LayoutBox(lines[i]).HasValue)
            .OrderBy(i => LayoutBox(lines[i])!.Value.Top).ToList();
        var names = new List<int>();
        var previews = new HashSet<int>();
        for (int i = 0; i + 1 < order.Count; i++)
        {
            var name = order[i];
            var preview = order[i + 1];
            if (!lines[preview].Text.Trim().Contains(' ') || !IsContactPair(lines[name], lines[preview])) continue;
            names.Add(name);
            previews.Add(preview);
            i++;
        }
        if (names.Count < 3) return new Reading(lines);
        var first = LayoutBox(lines[names[0]])!.Value;
        var last = LayoutBox(lines[names[^1]])!.Value;
        if (last.Top <= first.Top) return new Reading(lines);
        if (!headed && previews.Count(i => IsCut(lines[i].Text)) < 2) return new Reading(lines);
        double slope = (last.Left - first.Left) / (last.Top - first.Top);
        bool InNameColumn(TextLine line) => LayoutBox(line) is { } box &&
            Math.Abs(box.Left - (first.Left + slope * (box.Top - first.Top))) <= 0.06;
        if (!names.All(i => InNameColumn(lines[i]))) return new Reading(lines);
        // Listeyi uzun önizlemeler doğruladı. Aynı satır düzenindeki "Okay" gibi kısa bir yanıt
        // tek başına kişi adına benziyor diye ad sayılmaz; önizlemesi olarak kalır.
        for (int i = 0; i + 1 < order.Count; i++)
        {
            var name = order[i];
            var preview = order[i + 1];
            if (!InNameColumn(lines[name]) || previews.Contains(name) || names.Contains(preview) ||
                lines[preview].Text.Trim().Contains(' ') || !LooksLikeName(lines[preview].Text) ||
                !IsContactPair(lines[name], lines[preview])) continue;
            names.Add(name);
            previews.Add(preview);
            i++;
        }
        // Önizlemesi boş kişilerin adı da aynı eğik sütunda, aynı satır düzenindedir.
        return new Reading(lines.Select((line, index) =>
            previews.Contains(index) ? PreviewPunctuation(line) with { ListRole = ScreenPhoneCard.Role.Preview }
            : SectionLabel(line.Text) is not null ? line with { ListRole = ScreenPhoneCard.Role.Heading }
            : InNameColumn(line) && (names.Contains(index) || LooksLikeName(line.Text)) ? line with { ListRole = ScreenPhoneCard.Role.Contact }
            : line with { ListRole = ScreenPhoneCard.Role.Preview }));
    }

    /// <summary>Telefonun "…" ile kestiği önizleme ("Hey this is Joey from Coa..."). Mac ile aynı.</summary>
    internal static bool IsCut(string text)
    {
        var value = text.Trim();
        return value.EndsWith("...", StringComparison.Ordinal) || value.EndsWith('…');
    }

    private static readonly HashSet<string> DanglingWords =
    [
        "a", "an", "the", "of", "to", "in", "on", "at", "for", "from", "with", "by", "about", "and", "but", "or", "so",
        "than", "my", "your", "his", "her", "its", "our", "their", "this", "that", "is", "are", "was", "were", "be", "i",
        "you", "we", "they", "it", "if", "as",
    ];

    /// <summary>
    /// Kesik önizlemenin çevrilecek kısmı: telefon son sözcüğü yarıda keser ("Coa...", "in t..."), yazı tanıma da üç
    /// noktanın önündeki boşluğu güvenilir okumaz; son sözcük her zaman atılır, ardından kalan bağlaç/edat ("from", "in",
    /// "than") da. Çeviri "..." ile biter (<see cref="CutTranslation"/>): önceden "Hey this is Joey from Coa..." "Selam,
    /// ben Coa'dan Joey..." çevriliyor, yarım sözcük uyduruluyordu. Kesik değilse ya da geriye bir sözcük bile kalmazsa
    /// null. Mac ile aynı.
    /// </summary>
    internal static string? CutInput(string text)
    {
        if (!IsCut(text)) return null;
        var value = text.Trim().TrimEnd('.', '…');
        var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count < 2) return null;
        words.RemoveAt(words.Count - 1);
        while (words.Count > 1 && DanglingWords.Contains(TrimPunctuation(words[^1].ToLowerInvariant()))) words.RemoveAt(words.Count - 1);
        var core = string.Join(' ', words).TrimEnd(',', ';', ':', '-', '–', '—').Trim();
        return core.Any(char.IsLetter) ? core : null;
    }

    private static string TrimPunctuation(string word)
    {
        int start = 0, end = word.Length;
        while (start < end && char.IsPunctuation(word[start])) start++;
        while (end > start && char.IsPunctuation(word[end - 1])) end--;
        return word[start..end];
    }

    /// <summary>Kesik önizlemenin çevirisi: sondaki noktalama atılıp "..." eklenir (telefondaki gibi kesik görünür). Mac ile aynı.</summary>
    internal static string CutTranslation(string translation) =>
        translation.Trim().TrimEnd('.', ',', ';', ':', '…', '–', '—', '-').Trim() + "...";

    private static TextLine PreviewPunctuation(TextLine line)
    {
        // Liste sağ kenarda kesme işareti olarak üç nokta kullanıyor. Yalnız noktalama düzeltilir:
        // kırpılmış sözcüğün harfleri ve "ttv." gibi olası kısaltmalar tahmin edilmez.
        if (LayoutBox(line) is not { Right: >= .76 }) return line;
        int end = line.Text.Length, start = end;
        while (start > 0 && line.Text[start - 1] is '.' or ',' or '„') start--;
        if (end - start is < 2 or > 3 || line.Text[start] != '.') return line;
        int wordEnd = start;
        while (wordEnd > 0 && char.IsWhiteSpace(line.Text[wordEnd - 1])) wordEnd--;
        if (wordEnd == 0 || !char.IsLetter(line.Text[wordEnd - 1])) return line;
        if (wordEnd == start && line.Text.EndsWith("...", StringComparison.Ordinal)) return line;
        return line with { Text = line.Text[..wordEnd] + "..." };
    }

    private static TextBox? LayoutBox(TextLine line) => line.PhoneBox ?? line.Box;

    private static readonly string[] Headings = ["Messages", "Suggested", "Suggestions", "Öneriler", "Mesajlar"];

    /// <summary>
    /// Sohbet listesinin bölüm başlığı ("Suggested", "Messages"); değilse null. Başlığın yanındaki simge yazı tanımada tek
    /// harf ya da işaret olarak okunur ("g Suggested", "✱ Suggested", "Messages ☆"): o artık atılır. Önceden "g Suggested"
    /// başlık sayılmıyor, sallanan telefonda liste her okumada bir liste bir sohbet çiziliyor, TV'de "g Önerilen" yazıyordu.
    /// Simge artığı <see cref="ButtonIcons.IsIconToken"/> ile aynı tanımdadır (tek harf ya da işaret). Mac ile aynı.
    /// </summary>
    internal static string? SectionLabel(string text)
    {
        int first = 0, end = text.Length;
        while (first < end && !char.IsLetter(text[first])) first++;
        while (end > first && !char.IsLetter(text[end - 1])) end--;
        var value = text[first..end];
        if (Headings.Contains(value)) return value;
        var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 3 || words.FirstOrDefault(Headings.Contains) is not { } heading) return null;
        var rest = words.Where(w => w != heading).ToArray();
        return rest.Length == words.Length - 1 &&
            rest.All(w => w.Length == 1 || ButtonIcons.IsIconToken(w) || !w.Any(char.IsLetter)) ? heading : null;
    }

    private static bool LooksLikeName(string text)
    {
        // Rehber, parantez içinde cihaz etiketi de gösterebilir.
        var label = text.Split('(', 2)[0].Trim();
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return SectionLabel(label) is null && words.Length is >= 1 and <= 4 && char.IsUpper(words[0][0]) &&
            words.All(w => (char.IsUpper(w[0]) || w is "de" or "du" or "of") &&
                           w.All(c => char.IsLetter(c) || c is '-' or '\''));
    }

    internal static bool IsMessagePreview(string text) =>
        text.Any(char.IsWhiteSpace) && SectionLabel(text) is null && !LooksLikeName(text);

    private static bool IsContactPair(TextLine name, TextLine preview)
    {
        var label = name.Text.Trim();
        bool lowerName = label.Length is >= 2 and <= 18 && label.All(char.IsLower);
        if (!LooksLikeName(label) && !lowerName) return false;
        var a = LayoutBox(name)!.Value;
        var b = LayoutBox(preview)!.Value;
        var h = a.Bottom - a.Top;
        return a.Left is > 0.10 and < 0.48 && Math.Abs(a.Left - b.Left) <= 0.06 &&
            b.Top > a.Top && b.Top - a.Bottom < h * 0.7 && b.Bottom - b.Top < h * 1.8;
    }

    // Konuşmada karşı kenara yaslanan yeni balon, önceki satır küçük harfle bitse bile ayrı çevrilir.
    /// <summary>
    /// Telefon ekranının blokları, kendi yerlerinde. Ana ekranda uygulama adları (WhatUp, simge etiketleri) çevrilmez
    /// (<see cref="PhoneChat.WithoutAppNames"/>); saat ve hava harfsiz satır olarak zaten atılır.
    /// </summary>
    internal static IReadOnlyList<ScreenTranslation.Group> Groups(Reading reading)
    {
        var home = PhoneChat.IsHomeScreen(reading.Lines);
        reading = new Reading(PhoneChat.WithoutAppNames(reading.Lines));
        List<ScreenTranslation.Group> groups = [];
        List<TextLine> run = [];
        bool flat = reading.Lines.All(l => l.PhoneBox.HasValue);
        // Sohbet listesi (ForTranslation): her satır kendi satırıdır (ad ve altındaki önizleme ayrı), rolü okumadan gelir.
        bool listed = reading.Lines.Any(l => l.ListRole.HasValue);
        TextBox Layout(TextLine l) => (flat ? l.PhoneBox : l.Box)!.Value;
        void Finish()
        {
            if (run.Count == 0) return;
            if (flat)
            {
                var clean = new Reading(run).WithButtonIcons().WithoutButtonPrompts().WithoutOcrNoise(true).Lines
                    .Where(l => l.Box.HasValue && ScreenTranslation.IsWords(l.Text)).ToArray();
                if (clean.Length > 0)
                {
                    // Aynı balona sarılan satırlar birlikte çevrilir; TV konumu özgün oyun kutularından gelir.
                    var box = clean.Select(l => l.Box!.Value).Aggregate((a, b) => a.Union(b));
                    var heights = run.Select(l => Layout(l).Bottom - Layout(l).Top).Order().ToArray();
                    var icons = clean.SelectMany(l => l.Icons ?? []).ToArray();
                    // Düz ekrandaki yeri: sohbet panelinde tarafı (sol/sağ) ve sırası bundan çıkar (Roles).
                    var spot = run.Select(Layout).Aggregate((a, b) => a.Union(b));
                    groups.Add(new ScreenTranslation.Group([string.Join(' ', clean.Select(l => l.Text))], box,
                        heights[heights.Length / 2], icons.Length == 0 ? null : [icons])
                    {
                        Phone = new ScreenPhoneCard.Placement(
                            listed ? run[0].ListRole ?? ScreenPhoneCard.Role.Preview : ScreenPhoneCard.Role.Message, spot),
                    });
                }
            }
            else groups.AddRange(ScreenTranslation.Groups(new Reading(run), menuRows: false, keepIdentifiers: true));
            run.Clear();
        }
        foreach (var line in reading.Lines.Where(l => l.Box.HasValue).OrderBy(l => Layout(l).Top))
        {
            if (run.Count > 0)
            {
                var previous = run[^1];
                if (listed || BreaksBlock(Layout(previous), Layout(line), flat && EndsSentence(previous.Text))) Finish();
            }
            run.Add(line);
        }
        Finish();
        var kept = groups.Count <= ScreenTranslation.MaxGroups ? groups : groups.GetRange(0, ScreenTranslation.MaxGroups);
        if (listed && flat && kept.Count > 0) return kept;
        return Roles(kept, home);
    }

    /// <summary>
    /// Sohbet panelinde her grubun rolü ("Ekranı çevir"in <see cref="PhoneChat"/> kuralları, düz ekrandaki yerinden): en
    /// üstteki kısa ad satırı kişinin adıdır (çevrilmez), ortalı kısa satır not, sağa yaslı balon oyuncunun, sola yaslı
    /// karşı tarafın mesajı. Sohbet listesinde önizlemeler, ana ekranda bütün yazılar kendi yerinde kalır. Mac ile aynı.
    /// </summary>
    internal static IReadOnlyList<ScreenTranslation.Group> Roles(IReadOnlyList<ScreenTranslation.Group> groups, bool home)
    {
        if (!groups.All(group => group.Phone is not null)) return [.. groups.Select(group => group with { Phone = null })];
        var list = groups.Any(group => SectionLabel(string.Concat(group.Paragraphs)) is not null);
        var first = -1;
        for (var i = 0; i < groups.Count; i++)
            if (first < 0 || groups[i].Phone!.Spot.Top < groups[first].Phone!.Spot.Top) first = i;
        return [.. groups.Select((group, index) =>
        {
            var text = string.Join(' ', group.Paragraphs);
            var spot = group.Phone!.Spot;
            var row = new PhoneChat.Row([], spot, text);
            var role = home ? ScreenPhoneCard.Role.Home
                : index == first && !list && PhoneChat.IsHeader(row) ? ScreenPhoneCard.Role.Header
                : list ? (SectionLabel(text) is not null ? ScreenPhoneCard.Role.Heading : ScreenPhoneCard.Role.Preview)
                : PhoneChat.RoleOf([row]);
            return group with { Phone = new ScreenPhoneCard.Placement(role, spot) };
        })];
    }

    private static bool EndsSentence(string text) => text.TrimEnd().LastOrDefault() is '.' or '!' or '?';

    private static bool BreaksBlock(TextBox previous, TextBox next, bool afterFlatSentence)
    {
        // Düzeltilmiş telefon görüntüsünde satır arası dar, ardışık balonlar arasındaki boşluk daha geniştir.
        // Cümle sonunda bu aralığı kullan; aynı balonda alt satıra geçen ikinci cümle birlikte kalır.
        var lineHeight = Math.Max(previous.Bottom - previous.Top, next.Bottom - next.Top);
        if (next.Top - previous.Bottom > lineHeight * (afterFlatSentence ? 0.65 : 1)) return true;
        bool alignedLeft = Math.Abs(previous.Left - next.Left) <= 0.08;
        bool alignedRight = Math.Min(previous.Left, next.Left) > 0.25 && Math.Abs(previous.Right - next.Right) <= 0.045;
        return !alignedLeft && !alignedRight;
    }
}
