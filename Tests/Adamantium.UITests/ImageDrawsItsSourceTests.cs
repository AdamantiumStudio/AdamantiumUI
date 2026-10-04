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
            var candidate = Path.Combine(dir.FullName, "Adamantium.UI.Sandbox", "Textures", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("could not find Adamantium.UI.Sandbox/Textures/" + name);
    }

    private static ImageSource DrawnPicture(Image image)
    {
        var recorder = new Recorder();
        var window = new Window { Width = 400, Height = 300, Content = image };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
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
}
