using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics.Vector;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>A picture's pixels as PDF and XPS embed them.</summary>
public class PicturePixelsTests
{
    // As the shader does: brightness, contrast and saturation first, then the tint multiplied in - alpha too.
    [Test]
    public void APicturesTintAndAdjustment_ChangeItsPixelsAsTheRendererDoes()
    {
        byte[] white = [255, 255, 255, 255];
        PicturePixels.Recolor(white, new Color(255, 0, 0, 128), default);
        Assert.That(white, Is.EqualTo(new byte[] { 255, 0, 0, 128 }));

        byte[] gray = [128, 128, 128, 255];
        PicturePixels.Recolor(gray, new Color(255, 255, 255, 255), new Vector4<float>(0.25f, 0, 0, 0));
        Assert.That(gray[0], Is.EqualTo((byte)Math.Round((128 / 255.0 + 0.25) * 255)), "brightness added");

        byte[] red = [255, 0, 0, 255];
        PicturePixels.Recolor(red, new Color(255, 255, 255, 255), new Vector4<float>(0, 0, -1, 0));
        Assert.That(red[0], Is.EqualTo(red[1]).And.EqualTo(red[2]), "no saturation leaves the gray");
    }
}
