using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.Payloads;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>
/// A border around a material fill - the card of every Fluent popup - rides in the material's own record: the batch
/// takes the framed rect whole, so a ring it does not bake is a ring nobody draws. It used to bake the pen alone, and a
/// border is not a pen, so every acrylic card lost its edge and melted into what was behind it.
/// </summary>
[TestFixture]
public class MaterialFrameTests
{
    private static RectanglePayload Card(Brush border, Thickness thickness) =>
        new(new MaterialBrush(), new Rect(0, 0, 200, 120), new CornerRadius(6), border, thickness);

    private static MaterialRectItem Bake(RectanglePayload card, double opacity = 1.0)
    {
        Assert.That(MaterialRectCollector.BakeItem(card, Matrix4x4F.Identity, opacity, transformSlot: 0, fadeSlot: -1,
            source: null, clipSlot: -1, out var item), Is.True);
        return item;
    }

    [Test]
    public void AUniformBorderIsBakedAsARingInsideTheOutline()
    {
        var item = Bake(Card(new SolidColorBrush(Colors.Red), new Thickness(2)));

        Assert.Multiple(() =>
        {
            Assert.That(item.StrokeColor, Is.EqualTo(new Vector4F(1, 0, 0, 1)), "the border's color");
            Assert.That(item.Stroke0.X, Is.EqualTo(2f), "its width");
            Assert.That(item.Stroke0.Y, Is.EqualTo(-1f), "wholly inside the outline, where a border lies");
        });
    }

    [Test]
    public void TheBorderFadesWithTheElement()
    {
        var item = Bake(Card(new SolidColorBrush(Colors.Red), new Thickness(1)), opacity: 0.5);

        Assert.That(item.StrokeColor.W, Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void WithoutABorderThereIsNoRing()
    {
        var item = Bake(Card(null, new Thickness(0)));

        Assert.That(item.Stroke0.X, Is.Zero);
    }
}
