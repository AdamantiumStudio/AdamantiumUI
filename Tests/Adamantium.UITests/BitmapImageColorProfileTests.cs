using System;
using System.IO;
using System.Threading.Tasks;
using Adamantium.Imaging;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Media.Imaging;
using Image = Adamantium.UI.Controls.Image;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>BitmapImage.ApplyColorProfile: the profile a file holds applied as it is read, whichever way round the
/// properties are set; off, the pixels as stored.</summary>
[TestFixture]
public class BitmapImageColorProfileTests
{
    private static string Texture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Adamantium.UI.Sandbox.Content", "Textures", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("could not find Adamantium.UI.Sandbox.Content/Textures/" + name);
    }

    private static byte[] Read(string path, bool apply) =>
        BitmapLoader.Load(path, new BitmapLoadOptions { ApplyColorProfile = apply }).GetRawPixels(0);

    [Test]
    public async Task ByDefaultThePixelsAreAsStored()
    {
        var path = Texture("color-wide.jpg");
        var image = new BitmapImage(new Uri(path));

        await image.EnsureLoadedAsync();

        Assert.That(image.ApplyColorProfile, Is.False);
        Assert.That(image.PixelBytes, Is.EqualTo(Read(path, false)));
        Assert.That(image.PixelBytes, Is.Not.EqualTo(Read(path, true)), "the file's profile is not sRGB");
    }

    [Test]
    public async Task TheProfileIsAppliedWhenSetBeforeTheFile()
    {
        var path = Texture("color-wide.jpg");
        var image = new BitmapImage { ApplyColorProfile = true, UriSource = new Uri(path) };

        await image.EnsureLoadedAsync();

        Assert.That(image.PixelBytes, Is.EqualTo(Read(path, true)));
    }

    [Test]
    public async Task TheProfileIsAppliedWhenSetAfterTheFileAsMarkupMaySetIt()
    {
        var path = Texture("color-wide.jpg");
        var image = new BitmapImage { UriSource = new Uri(path) };
        var first = image.LoadTask;
        image.ApplyColorProfile = true;

        await first;

        Assert.That(image.PixelBytes, Is.EqualTo(Read(path, true)), "the first load waits for the one that replaced it");
        Assert.That(image.IsLoaded, Is.True);
    }

    [Test]
    public async Task ATurnAfterTheFileLoadedReplacesItsPixelsAndKeptLevels()
    {
        var path = Texture("photo-layers.jp2");
        var image = new BitmapImage(new Uri(path));
        await image.EnsureLoadedAsync();
        Assert.That(image.GetLevel(1, 0), Is.Not.Null);
        var reloads = 0;
        image.Reloaded += (_, _) => reloads++;

        image.UriSource = new Uri(Texture("color-wide.jpg"));
        await image.EnsureLoadedAsync();
        image.ApplyColorProfile = true;
        await image.EnsureLoadedAsync();

        Assert.That(reloads, Is.EqualTo(2));
        Assert.That(image.FindLevel(1, 0), Is.Null, "the old file's levels are gone");
        Assert.That(image.MipLevelsCount, Is.EqualTo(0u));
        Assert.That(image.PixelBytes, Is.EqualTo(Read(Texture("color-wide.jpg"), true)));
    }

    [Test]
    public async Task AnImageShowingThePictureTakesItAgainOnceItIsReadAgain()
    {
        var bitmap = new BitmapImage(new Uri(Texture("photo-layers.jp2")));
        var image = new Image { Source = bitmap };
        var window = new Window { Width = 400, Height = 300, Content = image };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        await bitmap.EnsureLoadedAsync();
        await Eventually(() => image.MipLevelCount > 0);

        bitmap.UriSource = new Uri(Texture("color-wide.jpg"));
        await bitmap.EnsureLoadedAsync();

        await Eventually(() => image.MipLevelCount == 0);
    }

    [Test]
    public async Task APathFromTheRootDoesNotThrowFromTheSetter()
    {
        var image = new BitmapImage();

        Assert.DoesNotThrow(() => image.UriSource = new Uri("/no-such-picture.png", UriKind.RelativeOrAbsolute));
        await image.EnsureLoadedAsync();

        Assert.That(image.IsLoaded, Is.False);
        Assert.That(image.LoadError, Is.InstanceOf<IOException>());
    }

    [Test]
    public async Task ClearingTheFileDropsTheLoadUnderWay()
    {
        var image = new BitmapImage(new Uri(Texture("color-wide.jpg")));
        var load = image.LoadTask;

        image.UriSource = null;
        await load;

        Assert.That(image.IsLoaded, Is.False);
        Assert.That(image.FrameCount, Is.EqualTo(0u));
    }

    [Test]
    public async Task AReadThatFailsAfterOneThatLoadedLeavesNothingLoaded()
    {
        var image = new BitmapImage(new Uri(Texture("color-wide.jpg")));
        await image.EnsureLoadedAsync();

        image.UriSource = new Uri(Path.Combine(Path.GetTempPath(), "no-such-picture-" + Guid.NewGuid() + ".png"));
        await image.EnsureLoadedAsync();

        Assert.That(image.IsLoaded, Is.False);
        Assert.That(image.LoadError, Is.Not.Null);
        Assert.That(image.FrameCount, Is.EqualTo(0u));
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True);
    }

    [Test]
    public async Task ARelativeFileIsTheApplicationsAsMarkupWritesIt()
    {
        var path = Texture("color-wide.jpg");
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
        var image = new BitmapImage { ApplyColorProfile = true, UriSource = new Uri(relative, UriKind.Relative) };

        await image.EnsureLoadedAsync();

        Assert.That(image.LoadError, Is.Null);
        Assert.That(image.PixelBytes, Is.EqualTo(Read(path, true)));
    }
}
