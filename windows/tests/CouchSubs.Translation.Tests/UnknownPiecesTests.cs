// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Translation.Tests;

/// <summary>Model dosyası gerekmez: örnekler gerçek OPUS-MT çıktılarından (84 satırlık GTA 6 ölçümü).</summary>
public class UnknownPiecesTests
{
    [Theory]
    // ⁇ kaynaktaki sözcükten doldurulur (joker, büyük/küçük harf ayrımsız).
    [InlineData("I mean, we're not running a daycare here, coño.", "Burada bir kreş işletmiyoruz, co⁇o.",
        "Burada bir kreş işletmiyoruz, coño.")]
    [InlineData("Mañana, buddy.", "Ma⁇ana, dostum.", "Mañana, dostum.")]
    [InlineData("José went to the piñata party.", "Jos⁇ pi⁇ata partisine gitti.", "José piñata partisine gitti.")]
    [InlineData("Pokémon cards are worth a fortune.", "Pok⁇mon kartları bir servet değerindedir.",
        "Pokémon kartları bir servet değerindedir.")]
    [InlineData("MAÑANA!", "MA⁇ANA!", "MAÑANA!")]
    // Türkçe ek ve kesme korunur.
    [InlineData("Go to Mañana.", "Ma⁇ana'ya git.", "Mañana'ya git.")]
    [InlineData("It costs 50€.", "50⁇ tutuyor.", "50€ tutuyor.")]
    // Kaynakta yoksa Türkçe tablo.
    [InlineData("Word is he got caught with his hands down an inmate's pants.",
        "Söylentiye göre elleri bir mahk⁇mun pantolonundan aşağı doğru yakalanmış.",
        "Söylentiye göre elleri bir mahkûmun pantolonundan aşağı doğru yakalanmış.")]
    [InlineData("He's still here.", "H⁇l⁇ burada.", "Hâlâ burada.")]
    [InlineData("No profit.", "K⁇r yok.", "Kâr yok.")]
    // Hiçbiri uymazsa ⁇ atılır; boşluk tek kalır.
    [InlineData("Wait—what?", "Bekle ⁇ ne?", "Bekle ne?")]
    [InlineData("Something odd.", "Tuh⁇f bir şey.", "Tuhf bir şey.")]
    // ⁇ yalnız ASCII dışı karakterden doğar: kaynaktaki "since" "⁇nce"ye uydurulmaz.
    [InlineData("Ever since then.", "O ⁇nce.", "O nce.")]
    public void FillsUnknownPieces(string source, string output, string expected) =>
        Assert.Equal(expected, UnknownPieces.Repair(source, output));

    [Theory]
    // Sadeleşmiş kaynak sözcük çıktıya aynen geçtiyse özgün yazım geri gelir.
    [InlineData("Mañana, buddy.", "Manana, dostum.", "Mañana, dostum.")]
    [InlineData("I mean, we're not running a daycare here, coño.", "Yani, burada kreş işletmiyoruz, Cono.",
        "Yani, burada kreş işletmiyoruz, coño.")]
    [InlineData("Señor José is here.", "Jose Bey burada.", "José Bey burada.")]
    [InlineData("¡Mañana!", "MANANA!", "MAÑANA!")]
    [InlineData("coño, again.", "cono, yine.", "Coño, yine.")]
    // Kaynakta aksansız olan sözcüğe dokunulmaz.
    [InlineData("Manana is a name.", "Manana bir isim.", "Manana bir isim.")]
    [InlineData("Let's go.", "Hadi gidelim.", "Hadi gidelim.")]
    public void RestoresOriginalSpelling(string source, string output, string expected) =>
        Assert.Equal(expected, UnknownPieces.Repair(source, output));

    /// <summary>Gerçek sözlükteki gibi: Türkçe harfler, ASCII ve "â" bilinir; ñ, é, û, ß bilinmez.</summary>
    private static bool Known(char c) => c < 0x80 || "çğıöşüÇĞİÖŞÜâî“”‘’–".Contains(c);

    [Theory]
    [InlineData("Mañana, buddy.", "Manana, buddy.")]
    [InlineData("Señor, the café is closed.", "Senor, the cafe is closed.")]
    [InlineData("Pokémon", "Pokemon")]
    // Birleşik aksan (e + ◌́) önce birleştirilir.
    [InlineData("café", "cafe")]
    [InlineData("«Run»", "“Run»")]
    // Ayrışmayan (ß, €) ve bilinen karakterlere dokunulmaz; yer tutucular ASCII'dir.
    [InlineData("Straße 5, 50€", "Straße 5, 50€")]
    [InlineData("Zarmon, take the Velkor.", "Zarmon, take the Velkor.")]
    [InlineData("I was—I mean…", "I was—I mean…")]
    public void SimplifiesCharactersTheModelNeverSaw(string text, string expected) =>
        Assert.Equal(expected, UnknownPieces.Simplify(text, Known));

    [Fact]
    public void LeavesCleanOutputAlone()
    {
        const string output = "Lanet olası türbin, Jason! Yukarı bak.";
        Assert.Same(output, UnknownPieces.Repair("The fuckin' turbine, Jason! Look up.", output));
    }
}
