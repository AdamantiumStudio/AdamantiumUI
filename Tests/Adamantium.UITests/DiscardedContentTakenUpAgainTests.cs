using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Content let go of is released at idle time, and what is taken up again before then must come back alive - a docking
/// pane's body moving from one panel to another was let go of by the old panel and shown by the new one, dead: its
/// bindings closed, its labels empty.
/// </summary>
[TestFixture]
public class DiscardedContentTakenUpAgainTests
{
    private sealed class Root : Grid, IRootVisualComponent
    {
        public Vector2 PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2 point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private sealed class Model : INotifyPropertyChanged
    {
        private string _name = "first";

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }

    private static (Border Body, TextBlock Label, Model Source) Body()
    {
        var source = new Model();
        var label = new TextBlock();
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Name)) { Source = source });
        return (new Border { Child = label }, label, source);
    }

    [Test]
    public void ContentTakenUpBeforeItsRelease_ComesBackAlive()
    {
        var (body, label, source) = Body();
        var root = new Root();

        DiscardedVisuals.Publish(new IFundamentalUIComponent[] { body, label });   // as a panel letting it go does
        root.Children.Add(body);                                                    // ...and another panel takes it up
        DiscardedVisuals.Drain(int.MaxValue);

        source.Name = "second";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(body.IsDiscarded, Is.False);
            Assert.That(label.IsDiscarded, Is.False);
            Assert.That(label.Text, Is.EqualTo("second"), "its binding was closed");
        });
    }

    [Test]
    public void ContentNobodyTakesUp_IsReleased()
    {
        var (body, label, source) = Body();

        DiscardedVisuals.Publish(new IFundamentalUIComponent[] { body, label });
        DiscardedVisuals.Drain(int.MaxValue);

        source.Name = "second";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(label.IsDiscarded, Is.True);
            Assert.That(label.Text, Is.EqualTo("first"), "a released element no longer listens");
        });
    }
}
