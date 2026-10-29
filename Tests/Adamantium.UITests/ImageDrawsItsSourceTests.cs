using System;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.UI.Controls;
using Image = Adamantium.UI.Controls.Image;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A still picture is drawn from its source, so every image showing it shares one texture.</summary>
[TestFixture]
public class ImageDrawsItsSourceTests
{
    private sealed class Recorder : IDrawingContext
    {
        public RecordingDrawingSession Session { get; } = new();

        public IDrawingSession ForControl(IUIComponent component) => Session;
    }

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

    private static ImageSource DrawnPicture(Image image)
    {
        var recorder = new Recorder();
        var window = new Window { Width = 400, Height = 300, Content = image };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        image.Render(recorder);

        return recorder.Session.Rectangles.Select(r => r.Brush).OfType<ImageBrush>().Single().Source;
    }

    private static ImageSource DrawnAgain(Image image)
    {
        var recorder = new Recorder();
        image.InvalidateRender(false);
        image.Render(recorder);

        return recorder.Session.Rectangles.Select(r => r.Brush).OfType<ImageBrush>().Single().Source;
    }

    [Test]
    public void TwoImagesOfOneStillPictureDrawThePictureItself()
    {
        var picture = new BitmapImage(BitmapLoader.Load(Texture("texture2.jpg")));

        Assert.Multiple(() =>
        {
            Assert.That(DrawnPicture(new Image { Width = 64, Height = 64, Source = picture }), Is.SameAs(picture),
                "the image drew a copy of the picture, and a copy is a texture of its own");
            Assert.That(DrawnPicture(new Image { Width = 64, Height = 64, Source = picture }), Is.SameAs(picture),
                "a second image of the same picture drew a second copy");
        });
    }

    [Test]
    public void APictureLargerThanTheContentLimit_IsDrawnShrunk_AndKeepsItsOwnPixels()
    {
        var pixels = new byte[1024 * 512 * 4];
        var picture = new BitmapImage(new FrameBitmap(1024, 512, SurfaceFormat.R8G8B8A8.UNorm, [pixels]));
        var image = new Image { Width = 64, Height = 64, Source = picture, ContentPixelLimit = 256 };

        var drawn = (BitmapSource)DrawnPicture(image);

        Assert.Multiple(() =>
        {
            Assert.That((drawn.PixelWidth, drawn.PixelHeight), Is.EqualTo((256u, 128u)));
            Assert.That((drawn.Width, drawn.Height), Is.EqualTo((picture.Width, picture.Height)), "its size in the layout");
            Assert.That(picture.PixelBytes, Is.SameAs(pixels), "the source kept its own pixels");
            Assert.That(DrawnAgain(image), Is.SameAs(drawn), "the copy is made once");
        });
    }

    [Test]
    public void APictureDrawnAtItsOwnSize_IsNotShrunk()
    {
        var picture = new BitmapImage(new FrameBitmap(1024, 512, SurfaceFormat.R8G8B8A8.UNorm, [new byte[1024 * 512 * 4]]));

        Assert.That(DrawnPicture(new Image
        {
            Width = 64,
            Height = 64,
            Source = picture,
            ContentPixelLimit = 256,
            Stretch = Stretch.None
        }), Is.SameAs(picture));
    }

    [Test]
    public void APictureWithinTheContentLimit_IsDrawnItself()
    {
        var picture = new BitmapImage(new FrameBitmap(64, 32, SurfaceFormat.R8G8B8A8.UNorm, [new byte[64 * 32 * 4]]));

        Assert.That(DrawnPicture(new Image { Width = 64, Height = 64, Source = picture, ContentPixelLimit = 256 }),
            Is.SameAs(picture));
    }

    [Test]
    public void TwoImagesOfOneMipLevelDrawTheSameFrame()
    {
        var picture = new BitmapImage(BitmapLoader.Load(Texture("balls.dds")));
        Assume.That(picture.MipLevelsCount, Is.GreaterThan(1), "precondition: the file has mip levels");

        var first = DrawnPicture(new Image { Width = 64, Height = 64, Source = picture, MipLevel = 1 });
        var second = DrawnPicture(new Image { Width = 64, Height = 64, Source = picture, MipLevel = 1 });

        Assert.That(second, Is.SameAs(first), "each image of the same mip level built its own frame and texture");
    }
}
