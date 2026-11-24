using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// An overlay window with an owner - a control in the 800x600 window, at 100,100, 400x300: hidden while the owner is not
// shown, back where it was as it is; dragged, resized and kept inside it - or anywhere, not staying inside it.
[TestFixture]
public class OverlayWindowOwnerTests
{
    private Window _window;
    private Canvas _canvas;
    private Button _owner;

    [SetUp]
    public void ShowAnOwner()
    {
        _owner = new Button { Content = "Owner", Width = 400, Height = 300 };
        Canvas.SetLeft(_owner, 100);
        Canvas.SetTop(_owner, 100);
        _canvas = new Canvas { Children = { _owner } };
        _window = new Window { Width = 800, Height = 600, Content = _canvas };
        Settle();
    }

    [TearDown]
    public void LetGo() => FocusManager.Focus(null);

    [Test]
    public void TheOwnerCollapsed_TheWindowHides_AndComesBackWhereItWas()
    {
        var overlay = Shown(left: 150, top: 150);

        _owner.Visibility = Visibility.Collapsed;
        Settle();
        var hidden = overlay.Visibility;
        _owner.Visibility = Visibility.Visible;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.EqualTo(Visibility.Collapsed));
            Assert.That(overlay.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(overlay.IsOpen, Is.True, "hidden, not closed");
            Assert.That(overlay.WorldTransform.TranslationVector.X, Is.EqualTo(150).Within(0.5));
            Assert.That(overlay.WorldTransform.TranslationVector.Y, Is.EqualTo(150).Within(0.5));
        });
        overlay.Close();
    }

    [Test]
    public void TheOwnerTakenOutOfTheTree_TheWindowHides_AndPutBackComesBack()
    {
        var overlay = Shown(left: 150, top: 150);

        _canvas.Children.Remove(_owner);
        Settle();
        var hidden = overlay.Visibility;
        _canvas.Children.Add(_owner);
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.EqualTo(Visibility.Collapsed));
            Assert.That(overlay.Visibility, Is.EqualTo(Visibility.Visible));
        });
        overlay.Close();
    }

    [Test]
    public void HiddenWithTheKeyboardInIt_TheKeyboardGoesToTheOwner()
    {
        var (overlay, inside) = Overlay();
        Show(overlay, 150, 150);
        FocusManager.Focus(inside);
        var hadIt = FocusManager.Focused;

        _owner.Visibility = Visibility.Collapsed;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(hadIt, Is.SameAs(inside));
            Assert.That(FocusManager.Focused, Is.Null, "not left in a window not seen, nor given to the owner, not seen either");
        });
        overlay.Close();
    }

    [Test]
    public void Hidden_ItIsNoLongerTheActiveWindow()
    {
        var (overlay, _) = Overlay();
        overlay.ShowActivated = true;
        Show(overlay, 150, 150);
        var active = overlay.IsActive;

        _owner.Visibility = Visibility.Collapsed;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(active, Is.True);
            Assert.That(overlay.IsActive, Is.False, "an Escape pressed elsewhere does not close it");
        });
        overlay.Close();
    }

    [Test]
    public void TheOwnerTakenToAnotherWindow_ItHides()
    {
        var overlay = Shown(left: 150, top: 150);
        var other = new Window { Width = 400, Height = 300 };

        _canvas.Children.Remove(_owner);
        other.Content = _owner;
        WindowExtension.UpdateTree(other);
        Settle();

        Assert.That(overlay.Visibility, Is.EqualTo(Visibility.Collapsed), "not left standing over nothing in this window");
        overlay.Close();
        other.Close();
    }

    [Test]
    public void AModalOneHidden_ItsDimGoesWithIt()
    {
        var (overlay, _) = Overlay();
        overlay.IsModal = true;
        Show(overlay, 150, 150);
        var scrim = (UIComponent)overlay.ScrimPopup.Child;

        _owner.Visibility = Visibility.Collapsed;
        Settle();
        var hidden = scrim.Visibility;
        _owner.Visibility = Visibility.Visible;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.EqualTo(Visibility.Collapsed), "nothing behind held by a dialog not seen");
            Assert.That(scrim.Visibility, Is.EqualTo(Visibility.Visible));
        });
        overlay.Close();
    }

    [Test]
    public void ShownAgainOverTheWindowItsOwnerWasTakenTo_ItIsAsFarFromTheOwnerAsItWas()
    {
        var overlay = Shown(left: 150, top: 170);
        var other = new Window { Width = 600, Height = 400 };
        var otherCanvas = new Canvas();
        other.Content = otherCanvas;

        _canvas.Children.Remove(_owner);
        Canvas.SetLeft(_owner, 20);
        Canvas.SetTop(_owner, 30);
        otherCanvas.Children.Add(_owner);
        WindowExtension.UpdateTree(other);
        overlay.Close();
        other.ShowOverlayWindow(overlay);
        WindowExtension.UpdateTree(other);
        other.PopupLayer.UpdateLayout(new Size<double>(other.Width, other.Height));
        other.PopupLayer.UpdateLayout(new Size<double>(other.Width, other.Height));

        Assert.Multiple(() =>
        {
            Assert.That(overlay.WorldTransform.TranslationVector.X, Is.EqualTo(70).Within(0.5), "50 from the owner's left");
            Assert.That(overlay.WorldTransform.TranslationVector.Y, Is.EqualTo(100).Within(0.5), "70 from its top");
        });
        overlay.Close();
        other.Close();
    }

    [Test]
    public void TheOwnerMoved_TheWindowGoesWithIt()
    {
        var overlay = Shown(left: 150, top: 160);

        Canvas.SetLeft(_owner, 140);
        Canvas.SetTop(_owner, 120);
        Settle();
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(overlay.WorldTransform.TranslationVector.X, Is.EqualTo(190).Within(0.5), "as far from it as it was");
            Assert.That(overlay.WorldTransform.TranslationVector.Y, Is.EqualTo(180).Within(0.5));
        });
        overlay.Close();
    }

    [Test]
    public void KeptWithTheOwner_ItIsNotPlacedByHand_DraggedItIs()
    {
        var overlay = Shown(left: 280, top: 280);
        var placedByHand = 0;
        overlay.PlacedByHand += (_, _) => placedByHand++;

        _owner.Width = 150;
        Settle();
        Settle();
        var kept = placedByHand;
        overlay.MoveCard(new Vector2<double>(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(0), "moved in as the owner shrank: not by hand");
            Assert.That(placedByHand, Is.EqualTo(1), "dragged: by hand");
        });
        overlay.Close();
    }

    [Test]
    public void HiddenByAnyoneElse_ItIsNotShownAsTheOwnerIs()
    {
        var overlay = Shown(left: 150, top: 150);
        overlay.Visibility = Visibility.Collapsed;

        _owner.Visibility = Visibility.Collapsed;
        Settle();
        _owner.Visibility = Visibility.Visible;
        Settle();

        Assert.That(overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
        overlay.Close();
    }

    [Test]
    public void Dragged_ItStopsAtTheOwnersEdges()
    {
        var overlay = Shown(left: 150, top: 150);

        overlay.MoveCard(new Vector2<double>(-500, -500));
        Settle();
        var topLeft = overlay.WorldTransform.TranslationVector;
        overlay.MoveCard(new Vector2<double>(1000, 1000));
        Settle();
        var bottomRight = overlay.WorldTransform.TranslationVector;

        Assert.Multiple(() =>
        {
            Assert.That(topLeft.X, Is.EqualTo(100).Within(0.5));
            Assert.That(topLeft.Y, Is.EqualTo(100).Within(0.5));
            Assert.That(bottomRight.X, Is.EqualTo(500 - 200).Within(0.5), "its right at the owner's right");
            Assert.That(bottomRight.Y, Is.EqualTo(400 - 100).Within(0.5), "its bottom at the owner's bottom");
        });
        overlay.Close();
    }

    [Test]
    public void NotStayingInside_ItIsDraggedAnywhereInTheWindow()
    {
        var overlay = Shown(left: 150, top: 150);
        overlay.StaysInsideOwner = false;

        overlay.MoveCard(new Vector2<double>(-500, -500));
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(overlay.WorldTransform.TranslationVector.X, Is.EqualTo(0).Within(0.5));
            Assert.That(overlay.WorldTransform.TranslationVector.Y, Is.EqualTo(0).Within(0.5));
        });
        overlay.Close();
    }

    [Test]
    public void Resized_ItStopsAtTheOwnersEdges()
    {
        var overlay = Shown(left: 150, top: 150);

        overlay.ResizeCard(2000, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(overlay.Width, Is.EqualTo(500 - 150));
            Assert.That(overlay.Height, Is.EqualTo(400 - 150));
        });
        overlay.Close();
    }

    [Test]
    public void TheOwnerShrinks_TheWindowIsMovedInAndMadeSmaller()
    {
        var overlay = Shown(left: 280, top: 280);

        _owner.Width = 150;
        _owner.Height = 150;
        Settle();
        Settle();
        var at = overlay.WorldTransform.TranslationVector;

        Assert.Multiple(() =>
        {
            Assert.That(overlay.Width, Is.EqualTo(150), "no wider than the owner");
            Assert.That(at.X, Is.EqualTo(100).Within(0.5));
            Assert.That(at.Y + overlay.RenderSize.Height, Is.LessThanOrEqualTo(250.5), "its bottom inside the owner");
        });
        overlay.Close();
    }

    private OverlayWindow Shown(double left, double top)
    {
        var (overlay, _) = Overlay();
        Show(overlay, left, top);
        return overlay;
    }

    private void Show(OverlayWindow overlay, double left, double top)
    {
        overlay.Owner = _owner;
        overlay.StartupLocation = OverlayStartupLocation.TopLeft;
        overlay.Left = left;
        overlay.Top = top;
        _window.ShowOverlayWindow(overlay);
        Settle();
        Settle();
    }

    private void Settle()
    {
        WindowExtension.UpdateTree(_window);
        _window.PopupLayer.UpdateLayout(new Size<double>(_window.Width, _window.Height));
    }

    private static (OverlayWindow Overlay, Button Inside) Overlay()
    {
        var inside = new Button { Content = "Inside" };
        var overlay = new OverlayWindow
        {
            ShowActivated = false,
            OpenDuration = 0,
            MinWidth = 120,
            MinHeight = 60,
            Width = 200,
            Height = 100,
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = new StackPanel { Children = { inside } } })
        };
        return (overlay, inside);
    }
}
