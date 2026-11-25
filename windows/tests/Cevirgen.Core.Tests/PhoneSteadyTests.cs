// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Core.Tests;

/// <summary>
/// Aynı mesajın OCR'la bir iki harfi farklı okunuşu yeni mesaj sayılmaz: bilinen çevirisi kullanılır, çeviri gidip gelmez.
/// Sayı ya da olumsuzluk değiştiren okuma ve kısa satır aynı mesaj sayılmaz (Mac GamePhoneStabilityTests ve Android
/// PhoneSteadyTest ile aynı deneme).
/// </summary>
public class PhoneSteadyTests
{
    private static readonly Dictionary<string, string> Known = new()
    {
        ["Did you hear back from the guy at the marina?"] = "Marinadaki adamdan haber aldın mı?",
        ["Meet me at 5 by the docks"] = "Benimle 5'te rıhtımda buluş",
        ["I can do that tonight"] = "Bunu bu gece yapabilirim",
        ["Ok"] = "Tamam",
    };

    [Fact]
    public void SameMessageReadSlightlyDifferentlyKeepsItsTranslation()
    {
        Assert.Equal("Marinadaki adamdan haber aldın mı?", PhoneSteady.Translation("Did you hear back from the guy at the rnarina?", Known));
        Assert.Equal("Marinadaki adamdan haber aldın mı?", PhoneSteady.Translation("Did you hear back frorn the guy at the marina", Known));
        Assert.Null(PhoneSteady.Translation("Meet me at 6 by the docks", Known));
        Assert.Null(PhoneSteady.Translation("I can't do that tonight", Known));
        Assert.Null(PhoneSteady.Translation("On", Known));
        Assert.Null(PhoneSteady.Translation("Where are you going tonight?", Known));
        Assert.Equal("Tamam", PhoneSteady.Translation("Ok", Known));
    }

    [Fact]
    public void ScrollIsAllMatchedBubblesMovingTheSameWay()
    {
        // #1202, #1197: kaydırınca balonların hepsi aynı yöne kayar; bekletme ve konum sabitleme atlanır.
        (string, double)[] before = [("Where are you?", .30), ("At the docks.", .45), ("On my way.", .60)];
        (string, double)[] up = [("At the docks.", .25), ("On my way.", .40), ("Hurry up.", .55)];
        Assert.True(PhoneSteady.Scrolled(before, up, .04));
        (string, double)[] down = [("Hey.", .15), ("Where are you?", .38), ("At the docks.", .53)];
        Assert.True(PhoneSteady.Scrolled(before, down, .04));
        // Titreme (satır yüksekliğinin yarısından az) kaydırma değildir.
        (string, double)[] jitter = [("Where are you?", .31), ("At the docks.", .46), ("On my way.", .61)];
        Assert.False(PhoneSteady.Scrolled(before, jitter, .04));
        // Biri yukarı biri aşağı: kaydırma değil.
        (string, double)[] mixed = [("Where are you?", .20), ("At the docks.", .55)];
        Assert.False(PhoneSteady.Scrolled(before, mixed, .04));
        // Eşleşen balon yok ya da satır yüksekliği bilinmiyor.
        Assert.False(PhoneSteady.Scrolled(before, [("New chat.", .2)], .04));
        Assert.False(PhoneSteady.Scrolled(before, up, 0));
        // OCR'la biraz farklı okunan aynı mesaj da eşleşir.
        (string, double)[] typo = [("At the docks now, come.", .30)];
        Assert.True(PhoneSteady.Scrolled([("At the docks now, corne.", .50)], typo, .04));
    }
}
