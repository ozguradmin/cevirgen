// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;
using Xunit;

namespace CouchSubs.Core.Tests;

/// <summary>Yerel yama: TV'ye giden kutu üstte yığılı eski replikleri de kapsar.</summary>
public class StackAboveTests
{
    private static readonly TextBox Whole = new(0, 0, 1, 1);

    [Fact]
    public void NewLineBelowOldLineReportsWholeStack()
    {
        // GTA 6: eski replik üstte kalır, yeni replik altına gelir; aralarındaki boşluk ayrı blok yapar.
        var reading = new Reading(
        [
            new TextLine("Now, who the hell built this outhouse back here? She's a beaut.", new TextBox(0.1, 0.70, 0.9, 0.74)),
            new TextLine("Can never have too much storage space, am I right?", new TextBox(0.15, 0.80, 0.85, 0.84)),
        ]);
        TextBox? stack = null;
        var block = reading.SubtitleBlock(Whole, null, null, null, null, null, box => stack = box);
        Assert.Equal("Can never have too much storage space, am I right?", block.Text);
        Assert.NotNull(stack);
        Assert.Equal(0.70, stack!.Value.Top, 3);
        Assert.Equal(0.84, stack.Value.Bottom, 3);
    }

    [Fact]
    public void SingleLineKeepsItsOwnBox()
    {
        var reading = new Reading([new TextLine("He still here?", new TextBox(0.42, 0.80, 0.58, 0.84))]);
        TextBox? stack = null;
        reading.SubtitleBlock(Whole, null, null, null, null, null, box => stack = box);
        Assert.Equal(new TextBox(0.42, 0.80, 0.58, 0.84), stack);
    }

    [Fact]
    public void FarAwayBlockIsNotJoined()
    {
        // Üstte çok uzakta duran ortalı yazı (arada 4,5 satırdan fazla) yığına katılmaz.
        var reading = new Reading(
        [
            new TextLine("Some centered text far above here", new TextBox(0.3, 0.20, 0.7, 0.24)),
            new TextLine("Hold tight, we're almost there.", new TextBox(0.3, 0.80, 0.7, 0.84)),
        ]);
        TextBox? stack = null;
        reading.SubtitleBlock(Whole, null, null, null, null, null, box => stack = box);
        Assert.Equal(0.80, stack!.Value.Top, 3);
    }
}
