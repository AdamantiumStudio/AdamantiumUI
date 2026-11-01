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

/// <summary>An animation is laid into its <see cref="Image.Viewport"/> as a still picture is, and only the part of it the
/// element shows is drawn: a cropped animated GIF shows the crop, not the whole picture squeezed into the frame.</summary>
[TestFixture]
public class ImageCropsItsAnimationTests
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

    private static (Rect Destination, Rect SourceUv) DrawnFrame(Rect viewport)
    {
        var animation = new BitmapImage(BitmapLoader.Load(Texture("infinity.gif")));
        Assert.That(animation.FrameCount, Is.GreaterThan(1));
        var image = new Image { Width = 100, Height = 100, Stretch = Stretch.Fill, Source = animation, Viewport = viewport };
        var window = new Window { Width = 400, Height = 300, Content = image };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var recorder = new Recorder();

        image.Render(recorder);

        var frame = recorder.Session.Frames.Single();
        return (frame.Destination, frame.SourceUv);
    }

    [Test]
    public void ACroppedAnimation_DrawsOnlyThePartItsElementShows()
    {
        var (destination, sourceUv) = DrawnFrame(new Rect(-0.5, -0.5, 2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(destination, Is.EqualTo(new Rect(0, 0, 100, 100)), "where it is drawn");
            Assert.That(sourceUv, Is.EqualTo(new Rect(0.25, 0.25, 0.5, 0.5)), "the part of the frame drawn");
        });
    }

    [Test]
    public void AnAnimationNotCropped_DrawsTheWholeFrame()
    {
        var (destination, sourceUv) = DrawnFrame(new Rect(0, 0, 1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(destination, Is.EqualTo(new Rect(0, 0, 100, 100)));
            Assert.That(sourceUv, Is.EqualTo(new Rect(0, 0, 1, 1)));
        });
    }
}
