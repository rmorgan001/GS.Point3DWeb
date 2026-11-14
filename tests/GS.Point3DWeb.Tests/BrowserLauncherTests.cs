using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.Tests;

public class BrowserLauncherTests
{
    [Theory]
    [InlineData("http://localhost:5000", "http://localhost:5000")]
    [InlineData("http://0.0.0.0:5000", "http://localhost:5000")]
    [InlineData("http://[::]:5000", "http://localhost:5000")]
    [InlineData("http://*:5000", "http://localhost:5000")]
    [InlineData("http://+:5000", "http://localhost:5000")]
    [InlineData("http://192.168.1.20:5000", "http://192.168.1.20:5000")]
    public void An_address_on_every_interface_is_opened_as_localhost(string address, string expected)
        => Assert.Equal(expected, BrowserLauncher.PickUrl([address]));

    [Fact]
    public void Http_is_preferred_to_https()
        => Assert.Equal("http://localhost:5181", BrowserLauncher.PickUrl(["https://localhost:7110", "http://localhost:5181"]));

    [Fact]
    public void Https_alone_is_used()
        => Assert.Equal("https://localhost:7110", BrowserLauncher.PickUrl(["https://localhost:7110"]));

    [Fact]
    public void No_address_opens_nothing() => Assert.Null(BrowserLauncher.PickUrl([]));
}
