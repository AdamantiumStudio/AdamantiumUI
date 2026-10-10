using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A popup's content is laid out by the window's popup layer, never by a layout pass of its own, so what it
/// asked to be measured or arranged must not wait in a queue nobody drains - it held every element it named, removed
/// ones too, for as long as the popup lived.</summary>
[TestFixture]
public class OverlayLayoutQueueTests
{
    [Test]
    public void AnOpenPopupThatSettled_HoldsNothingInItsLayoutQueues()
    {
        var row = new Border { Height = 10 };
        var card = new StackPanel();
        card.Children.Add(row);
        var popup = new Popup { Child = card };
        var host = new StackPanel();
        host.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, Content = host };
        var size = new Size(400, 300);
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(size);

        row.Height = 20;
        window.PopupLayer.UpdateLayout(size);
        card.Children.Remove(row);
        window.PopupLayer.UpdateLayout(size);
        window.PopupLayer.UpdateLayout(size);

        var queued = LayoutManager.For(card).QueuedCounts();
        Assert.That((queued.Style, queued.Measure, queued.Arrange, queued.NextPass), Is.EqualTo((0, 0, 0, 0)));
    }

    [Test]
    public void AMeasureAskedForTheNextPass_InAnOpenPopup_RunsOnTheNextFrame()
    {
        var filler = new Filler();
        var popup = new Popup { Child = filler };
        var host = new StackPanel();
        host.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, Content = host };
        var size = new Size(400, 300);
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;

        window.PopupLayer.UpdateLayout(size);
        window.PopupLayer.UpdateLayout(size);
        window.PopupLayer.UpdateLayout(size);

        Assert.That(filler.Measured, Is.EqualTo(2), "measured once, then once more for the rest it deferred");
    }

    // Defers the rest of its fill to the next pass, as a virtualizing panel does.
    private sealed class Filler : Border
    {
        public int Measured;

        protected override Size MeasureOverride(Size availableSize)
        {
            if (++Measured == 1) LayoutManager.For(this).InvalidateMeasureNextPass(this);
            return new Size(10, 10);
        }
    }
}
