// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell.Tests;

public class OverlayAnchorTests
{
    // Life Is Strange, tanılama 2026-11-25_16-01-00/0025: 1920x1200 oyun, %125 ölçek (1536x960 birim); iki satırlık İngilizce
    // altyazı karenin %19,3–81,4 / %82,5–94,2'sinde.
    private static readonly ScreenArea Game = new(0, 0, 1536, 960);
    private static readonly TextBox English = new(0.1931, 0.825, 0.8137, 0.9417);
    // Çevirinin kutusu pencerenin içinde (24, 100) noktasında, 900x80.
    private static readonly ScreenArea Line = new(24, 100, 900, 80);

    private static (double Left, double Top) Place(SubtitlePlacement placement, TextBox? english = null, ScreenArea? game = null) =>
        OverlayAnchor.WindowPosition(placement, english ?? English, game ?? Game, Line)!.Value;

    [Fact]
    public void CoverPutsTheBoxOnTheMiddleOfTheEnglishLine()
    {
        var (left, top) = Place(SubtitlePlacement.Cover);
        // Kutunun ortası İngilizce satırın ortasında: x = (0.1931+0.8137)/2·1536 ≈ 773, y = (0.825+0.9417)/2·960 ≈ 848.
        Assert.Equal(773, left + Line.Left + Line.Width / 2, 0);
        Assert.Equal(848, top + Line.Top + Line.Height / 2, 0);
    }

    [Fact]
    public void AboveLeavesTheTvGapOverTheEnglishLine()
    {
        var (_, top) = Place(SubtitlePlacement.Above, new TextBox(0.2, 0.6, 0.8, 0.7));
        // İngilizcenin üstü 0.6·960 = 576; boşluk 960·%2,4 ≈ 23: kutunun altı 553'te.
        Assert.Equal(553, top + Line.Top + Line.Height);
    }

    [Fact]
    public void BelowNearTheBottomEdgeGoesAbove()
    {
        // İngilizce satır ekranın en altında (%94): altında yer yok, üstüne konur.
        var (_, top) = Place(SubtitlePlacement.Below);
        Assert.Equal(Math.Round(0.825 * 960) - 23, top + Line.Top + Line.Height, 0);
    }

    [Fact]
    public void AboveNearTheTopEdgeGoesBelow()
    {
        var (_, top) = Place(SubtitlePlacement.Above, new TextBox(0.2, 0.05, 0.8, 0.1));
        Assert.Equal(0.1 * 960 + 23, top + Line.Top, 0);
    }

    [Fact]
    public void FollowsAGameWindowThatDoesNotFillTheScreen()
    {
        // Pencereli oyun ekranın sağ altında: 800x450 birim, (700, 500).
        var game = new ScreenArea(700, 500, 800, 450);
        var (left, top) = Place(SubtitlePlacement.Cover, game: game);
        Assert.Equal(700 + (0.1931 + 0.8137) / 2 * 800, left + Line.Left + Line.Width / 2, 0);
        Assert.Equal(500 + (0.825 + 0.9417) / 2 * 450, top + Line.Top + Line.Height / 2, 0);
    }

    [Fact]
    public void FixedOrUnusableBoxesKeepTheSavedPlace()
    {
        Assert.Null(OverlayAnchor.WindowPosition(SubtitlePlacement.Fixed, English, Game, Line));
        // Altyazı olamayacak kadar büyük kutu (karenin yarısından fazlası).
        Assert.Null(OverlayAnchor.WindowPosition(SubtitlePlacement.Cover, new TextBox(0.1, 0.1, 0.9, 0.9), Game, Line));
        Assert.Null(OverlayAnchor.WindowPosition(SubtitlePlacement.Cover, new TextBox(0.5, 0.8, 0.5, 0.9), Game, Line));
        Assert.Null(OverlayAnchor.WindowPosition(SubtitlePlacement.Cover, English, new ScreenArea(0, 0, 0, 0), Line));
    }
}
