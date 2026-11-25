// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;
using Cevirgen.RemotePlay;

namespace Cevirgen.Shell.Tests;

/// <summary>
/// Konsol kartında PS5 "IP Adresi Gir": PS5'in adresi değişip ağda bulunamayınca yeni adres elle yazılır; kayıt anahtarlarıyla
/// birlikte o adrese taşınır ve kalıcı kaydedilir. Geçersiz adreste kayıt değişmez, açıklama görünür (Mac, iPhone, Android ile aynı).
/// </summary>
public class Ps5AddressTests
{
    private static RegisteredConsole Saved() =>
        new("192.168.1.253", "PS5-789", Enumerable.Repeat((byte)0x31, 16).ToArray(), Enumerable.Repeat((byte)2, 16).ToArray());

    [Theory]
    [InlineData("192.168.1.40", "192.168.1.40")]
    [InlineData(" 192,168,1,40 ", "192.168.1.40")]
    [InlineData("http://192.168.1.40:9295/", "192.168.1.40")]
    public void TypedAddressMovesTheSavedPs5AndKeepsItsKeys(string typed, string expected)
    {
        var saved = Saved();
        var moved = AppController.MovedConsole(saved, typed);
        Assert.NotNull(moved);
        Assert.Equal(expected, moved!.Host);
        Assert.Equal(saved.Name, moved.Name);
        Assert.Equal(saved.RegistKey, moved.RegistKey);
        Assert.Equal(saved.Morning, moved.Morning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ps5.local")]
    [InlineData("192.168.1")]
    [InlineData("192.168.1.300")]
    public void InvalidAddressIsRejected(string typed) => Assert.Null(AppController.MovedConsole(Saved(), typed));

    [Fact]
    public void ControllerSavesTheNewAddressAndShowsAProblemForAnInvalidOne()
    {
        var store = new MemoryStore();
        store.Save(StoreKeys.Console, Saved());
        var controller = AppControllerTests.Make(new FakeServices(), store);

        Assert.False(controller.UsePs5Address("not an address"));
        Assert.NotNull(controller.Ps5AddressProblem);
        Assert.Equal("192.168.1.253", controller.Console!.Host);

        Assert.True(controller.UsePs5Address("192.168.1.40"));
        Assert.Null(controller.Ps5AddressProblem);
        Assert.Equal("192.168.1.40", controller.Console!.Host);
        Assert.Equal("192.168.1.40", store.Load<RegisteredConsole>(StoreKeys.Console)!.Host);
    }
}
