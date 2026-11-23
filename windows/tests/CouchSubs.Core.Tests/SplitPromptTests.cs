// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;
using Xunit;

namespace CouchSubs.Core.Tests;

/// <summary>Çevirgen: yazı tanıma tuş ipucunu simgenin yerinden ikiye bölse de ipucu birleşik satırda tanınır.</summary>
public class SplitPromptTests
{
    [Fact]
    public void PromptSplitAtTheIconIsDroppedAfterJoining()
    {
        var reading = new Reading(
        [
            new TextLine("Press", new TextBox(0.30, 0.80, 0.36, 0.84)),
            new TextLine("to scope out the deal", new TextBox(0.40, 0.80, 0.70, 0.84)),
        ]);
        // Parçalar ayrı ayrı ipucuna benzemez.
        Assert.Equal(2, reading.WithoutButtonPrompts().Lines.Count);
        // Birleşince "Press to scope out the deal" ipucudur ve atılır.
        Assert.Empty(reading.JoiningRows().WithoutButtonPrompts().Lines);
    }

    [Fact]
    public void DialogueIsKept()
    {
        var reading = new Reading([new TextLine("Hold tight, we're almost there.", new TextBox(0.3, 0.8, 0.7, 0.84))]);
        Assert.Single(reading.JoiningRows().WithoutButtonPrompts().Lines);
    }
}
