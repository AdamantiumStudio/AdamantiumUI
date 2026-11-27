using Adamantium.Core.Events;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The pointer's point on a universe's surface, asked from the universe's thread: through where the surface
/// stood when its own thread last placed it - the same point a walk of the tree gives - and, the surface taken out of the
/// tree, by where it last stood instead of a walk that finds no window.</summary>
[TestFixture]
public class UniversePointToSurfaceTests
{
    [Test]
    public void APointOnThePanel_IsWhereTheTreeSaysItIs()
    {
        var (_, _, panel) = Placed();
        var output = Output(panel);

        var expected = panel.PointToClient(new PixelPoint(140, 90));

        Assert.That(output.PointToSurface(new Vector2F(140, 90)),
            Is.EqualTo(new Vector2F((float)expected.X, (float)expected.Y) * output.PixelsPerPoint));
    }

    [Test]
    public void TheWindowMoved_ThePointFollowsIt()
    {
        var (root, _, panel) = Placed();
        var output = Output(panel);

        root.Position = new PixelPoint(300, 200);
        var expected = panel.PointToClient(new PixelPoint(440, 290));

        Assert.That(output.PointToSurface(new Vector2F(440, 290)),
            Is.EqualTo(new Vector2F((float)expected.X, (float)expected.Y) * output.PixelsPerPoint));
    }

    [Test]
    public void ThePanelMovedByLayout_ThePointFollowsIt()
    {
        var (root, holder, panel) = Placed();
        var output = Output(panel);

        holder.Margin = new Thickness(60, 50, 0, 0);
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);
        var expected = panel.PointToClient(new PixelPoint(140, 90));

        Assert.That(output.PointToSurface(new Vector2F(140, 90)),
            Is.EqualTo(new Vector2F((float)expected.X, (float)expected.Y) * output.PixelsPerPoint));
    }

    [Test]
    public void ARenderTransformGivenAbove_ThePointFollowsIt()
    {
        var (_, holder, panel) = Placed();
        var output = Output(panel);

        holder.RenderTransform = new Transform { TranslateX = 25, TranslateY = 15 };
        var expected = panel.PointToClient(new PixelPoint(140, 90));

        Assert.That(output.PointToSurface(new Vector2F(140, 90)),
            Is.EqualTo(new Vector2F((float)expected.X, (float)expected.Y) * output.PixelsPerPoint));
        Assert.That(expected, Is.Not.EqualTo(new Vector2<double>(100, 60)), "the transform moved it");
    }

    [Test]
    public void ThePanelTakenOutOfTheTree_AnswersWhereItLastStood_WithoutWalkingTheTree()
    {
        var (root, holder, panel) = Placed();
        var output = Output(panel);
        var before = output.PointToSurface(new Vector2F(140, 90));

        root.Children.Remove(holder);
        WindowExtension.UpdateTree(root);

        Assert.That(() => output.PointToSurface(new Vector2F(140, 90)), Throws.Nothing);
        Assert.That(output.PointToSurface(new Vector2F(140, 90)), Is.EqualTo(before));
    }

    private static (MovableTestWindowRoot Root, Border Holder, RenderTargetPanel Panel) Placed()
    {
        var root = new MovableTestWindowRoot { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600 };
        var panel = new RenderTargetPanel { Width = 300, Height = 200 };
        var holder = new Border
        {
            Margin = new Thickness(40, 30, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Child = panel
        };
        root.Children.Add(holder);
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);
        return (root, holder, panel);
    }

    private static RenderTargetUniverseOutput Output(RenderTargetPanel panel)
    {
        var output = new RenderTargetUniverseOutput(new EventAggregator(), new OutputContext(panel));
        output.Input = new InputWormhole(output, new GamepadHub(new NoGamepadBackend(), null));
        return output;
    }
}
