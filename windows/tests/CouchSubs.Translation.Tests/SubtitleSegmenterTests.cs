// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Translation.Tests;

public class SubtitleSegmenterTests
{
    /// <summary>Çevrilecek parçalar köşeli parantezde, kalanlar olduğu gibi.</summary>
    private static string Render(string text) =>
        string.Concat(SubtitleSegmenter.Split(text).Select(p => p.Translate ? $"[{p.Text}]" : p.Text));

    [Theory]
    [InlineData("Wait!", "[Wait!]")]
    [InlineData("Boy! Focus. The draugr are still coming.", "[Boy!] [Focus.] [The draugr are still coming.]")]
    [InlineData("What is that?! It looks like a door.", "[What is that?!] [It looks like a door.]")]
    [InlineData("\"Run!\" he said. Then nothing.", "[\"Run!\" he said.] [Then nothing.]")]
    [InlineData("I know… but we must go.", "[I know… but we must go.]")]
    [InlineData("Wait... what is that?", "[Wait... what is that?]")]
    [InlineData("Mr. Smith is here. Dr. Jones too.", "[Mr. Smith is here.] [Dr. Jones too.]")]
    [InlineData("No. We go now.", "[No.] [We go now.]")]
    [InlineData("Kratos.", "[Kratos.]")]
    // OPUS-MT çok cümleli satırın bir cümlesini düşürüyordu ("Drive! Drive!" → "Sür!"); her cümle ayrı çevrilir.
    [InlineData("Drive! Drive!", "[Drive!] [Drive!]")]
    [InlineData("Sicko! Mental case!", "[Sicko!] [Mental case!]")]
    [InlineData("You're done, you're done! Chill the fuck out.", "[You're done, you're done!] [Chill the fuck out.]")]
    [InlineData("Wait?! No... Stop!", "[Wait?!] [No...] [Stop!]")]
    // Ondalık, ad, kısaltma ve tırnak içindeki üç nokta bölmez.
    [InlineData("It's 3.5 miles to Vice City. Let's go.", "[It's 3.5 miles to Vice City.] [Let's go.]")]
    [InlineData("Mrs. Jones met St. Peter vs. Dr. No.", "[Mrs. Jones met St. Peter vs. Dr. No.]")]
    [InlineData("He's from the U.S. Army now.", "[He's from the U.S. Army now.]")]
    [InlineData("He said \"wait... Stop\" and left.", "[He said \"wait... Stop\" and left.]")]
    [InlineData("“Well… Maybe,” she said.", "[“Well… Maybe,” she said.]")]
    [InlineData("\"Wait...\" Then he left.", "[\"Wait...\"] [Then he left.]")]
    [InlineData("JASON: Drive! Drive!", "JASON: [Drive!] [Drive!]")]
    public void SplitsSentences(string text, string expected) => Assert.Equal(expected, Render(text));

    [Theory]
    [InlineData("ATREUS: Father, look over there!", "ATREUS: [Father, look over there!]")]
    [InlineData("ATREUS: Look! KRATOS: Stay behind me.", "ATREUS: [Look!] KRATOS: [Stay behind me.]")]
    [InlineData("SINDRI JR.: Hello there.", "SINDRI JR.: [Hello there.]")]
    [InlineData("Note: this is not a label.", "[Note: this is not a label.]")]
    [InlineData("Kratos: Stay close.", "Kratos: [Stay close.]")]
    [InlineData("Old Man: Who goes there?", "Old Man: [Who goes there?]")]
    [InlineData("Objective: Find the gate.", "[Objective: Find the gate.]")]
    public void KeepsSpeakerLabels(string text, string expected) => Assert.Equal(expected, Render(text));

    [Fact]
    public void JoinsWrappedLines()
    {
        Assert.Equal("[If we do not find the giant before nightfall, we will have to make camp.]",
            Render("If we do not find the giant before nightfall,\nwe will have to make camp."));
    }

    [Fact]
    public void KeepsLineBreaksBetweenSpeakers()
    {
        Assert.Equal("ATREUS: [Father, look!]\nKRATOS: [Stay behind me.]",
            Render("ATREUS: Father, look!\r\nKRATOS: Stay behind me."));
        Assert.Equal("- [Who are you?]\n- [A friend.]", Render("- Who are you?\n- A friend."));
    }

    [Theory]
    [InlineData("...", "...")]
    [InlineData("?!", "?!")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void DoesNotTranslatePiecesWithoutLetters(string text, string expected) => Assert.Equal(expected, Render(text));

    [Theory]
    [InlineData("THE GATE WILL NOT HOLD MUCH LONGER.", "The gate will not hold much longer.")]
    [InlineData("I KNOW, BUT I CAN'T.", "I know, but I can't.")]
    [InlineData("WAIT!", "WAIT!")]
    [InlineData("KRATOS.", "KRATOS.")]
    [InlineData("Who are you?", "Who are you?")]
    [InlineData("NASA is here.", "NASA is here.")]
    public void NormalizesAllCapsSentences(string sentence, string expected) =>
        Assert.Equal(expected, SubtitleSegmenter.NormalizeCase(sentence));

    [Fact]
    public void KnownNamesStayCapitalized() =>
        Assert.Equal("The gate will not hold, Atreus.",
            SubtitleSegmenter.NormalizeCase("THE GATE WILL NOT HOLD, ATREUS.", new HashSet<string> { "atreus" }));
}
