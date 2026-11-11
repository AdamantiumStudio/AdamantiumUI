using Adamantium.Imaging;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>How a picture's pixels are read, and the pictures made from pixels in memory; the miter limit as drawn.
/// </summary>
[TestFixture]
public class ImageSamplingTests
{
    [Test]
    public void APictureMadeFromPixelsIsReadyAtOnce_AndSmoothUnlessToldOtherwise()
    {
        var picture = new BitmapImage(2, 2, 1, 1, SurfaceFormat.R8G8B8A8.UNorm, new byte[16]);
        Assert.That(picture.IsLoaded, Is.True, "Image refuses to draw a picture that has not loaded");
        Assert.That(picture.Sampling, Is.EqualTo(ImageSampling.Smooth));

        picture.Sampling = ImageSampling.Pixelated;
        Assert.That(picture.Sampling, Is.EqualTo(ImageSampling.Pixelated));
    }

    [TestCase(double.NaN, 10)]
    [TestCase(0.5, 1)]
    [TestCase(4, 4)]
    [TestCase(1e6, Pen.MostMiterLimit)]
    public void AMiterLimitIsDrawnWithinItsRange(double limit, double drawn)
    {
        Assert.That(Pen.ClampMiterLimit(limit), Is.EqualTo(drawn));
    }
}
