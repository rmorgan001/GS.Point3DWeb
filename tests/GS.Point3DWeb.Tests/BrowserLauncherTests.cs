/* Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

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

    [Theory]
    [InlineData("http://unix:/tmp/app.sock")]
    [InlineData("http://pipe:/app")]
    public void A_socket_or_pipe_opens_nothing(string address) => Assert.Null(BrowserLauncher.PickUrl([address]));

    [Fact]
    public void A_socket_is_passed_over_for_an_address_a_browser_can_open()
        => Assert.Equal("http://localhost:5000", BrowserLauncher.PickUrl(["http://unix:/tmp/app.sock", "http://localhost:5000"]));
}
