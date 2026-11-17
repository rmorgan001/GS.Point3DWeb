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

using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.Tests;

/// <summary>A server's own background picture, kept in a temporary folder (never the real settings folder).</summary>
[Collection(nameof(BackgroundImagesTests))]   // BackgroundImages.Folder is shared: these run one at a time
public class BackgroundImagesTests : IDisposable
{
    private readonly string _before = BackgroundImages.Folder;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gsp3w-bg-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Guid.NewGuid().ToString();

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46];

    public BackgroundImagesTests() => BackgroundImages.Folder = _dir;

    public void Dispose()
    {
        BackgroundImages.Folder = _before;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Images_are_told_apart_by_their_first_bytes()
    {
        Assert.Equal(".png", BackgroundImages.KindOf(Png));
        Assert.Equal(".jpg", BackgroundImages.KindOf(Jpeg));
        Assert.Equal(".webp", BackgroundImages.KindOf("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(BackgroundImages.KindOf("<svg xmlns=\"http"u8));
        Assert.Null(BackgroundImages.KindOf([]));
    }

    [Fact]
    public async Task A_picture_is_kept_found_replaced_and_deleted()
    {
        Assert.True(await BackgroundImages.SaveAsync(_id, new MemoryStream(Png)));
        var file = BackgroundImages.Find(_id);
        Assert.NotNull(file);
        Assert.Equal(".png", file!.Extension);
        Assert.Equal("image/png", BackgroundImages.ContentType(file));
        Assert.StartsWith($"background/{_id}?v=", BackgroundImages.Url(_id));

        // A new picture of another kind replaces it.
        Assert.True(await BackgroundImages.SaveAsync(_id, new MemoryStream(Jpeg)));
        Assert.Equal(".jpg", BackgroundImages.Find(_id)!.Extension);
        Assert.Single(Directory.GetFiles(_dir));

        BackgroundImages.Delete(_id);
        Assert.Null(BackgroundImages.Find(_id));
        Assert.Null(BackgroundImages.Url(_id));
    }

    [Fact]
    public async Task Anything_but_a_picture_is_refused_and_keeps_the_old_one()
    {
        Assert.True(await BackgroundImages.SaveAsync(_id, new MemoryStream(Png)));
        Assert.False(await BackgroundImages.SaveAsync(_id, new MemoryStream("not a picture at all"u8.ToArray())));
        Assert.Equal(".png", BackgroundImages.Find(_id)!.Extension);
    }

    [Fact]
    public async Task A_picture_over_the_limit_is_refused()
    {
        var big = new byte[BackgroundImages.MaxBytes + 1];
        Png.CopyTo(big, 0);
        Assert.False(await BackgroundImages.SaveAsync(_id, new MemoryStream(big)));
        Assert.Null(BackgroundImages.Find(_id));
    }

    [Theory]
    [InlineData("..\\servers")]
    [InlineData("../../x")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Only_a_server_id_names_a_picture(string? id)
    {
        Assert.False(await BackgroundImages.SaveAsync(id!, new MemoryStream(Png)));
        Assert.Null(BackgroundImages.Find(id));
        BackgroundImages.Delete(id!);   // no throw
    }

    [Theory]
    [InlineData("Custom", "Custom")]
    [InlineData("custom", "Custom")]
    [InlineData("M33", "M33")]
    [InlineData("Nonsense", ModelAppearance.DefaultBackground)]
    [InlineData(null, ModelAppearance.DefaultBackground)]
    public void A_saved_background_can_be_the_users_own(string? saved, string expected)
        => Assert.Equal(expected, ModelAppearance.NormalizeBackground(saved));
}
