using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A tab that is its own container, taken out of one tab strip and put into another, keeps its bindings. The strip it
/// left parked it as though it would come back - hidden, bindings closed - and the strip it went to showed it with them
/// closed: a docking tab's label kept the size of the moment it was between panels.
/// </summary>
[TestFixture]
public class MovedTabKeepsItsBindingsTests
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

    // A tab control reduced to what decides realization: an items presenter hosting a TabPanel.
    private static TabControl Strip() => new()
    {
        ItemsPanel = new ItemsPanelTemplate(() => new TemplateResult
        {
            RootComponent = new TabPanel { Orientation = Orientation.Horizontal }
        }),
        Template = new ControlTemplate(() =>
        {
            var presenter = new ItemsPresenter();
            var result = new TemplateResult { RootComponent = presenter };
            result.RegisterName("PART_ItemsPresenter", presenter);
            return result;
        })
    };

    [Test]
    public void ATabMovedToAnotherStrip_StillFollowsItsSource()
    {
        var model = new Model();
        var tab = new TabItem { Width = 80, Height = 24 };
        tab.SetBinding(TabItem.HeaderProperty, new Binding(nameof(Model.Name)) { Source = model });

        var left = Strip();
        var right = Strip();
        var root = new Root { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600 };
        root.Children.Add(left);
        root.Children.Add(right);
        left.Items.Add(tab);

        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);

        // Out of the left strip, which is laid out again before the right one takes the tab up.
        left.Items.Remove(tab);
        (left.ItemsHostPanel as IMeasurableComponent)?.InvalidateArrange();
        WindowExtension.UpdateTree(root);

        right.Items.Add(tab);
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);

        model.Name = "second";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(tab.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(tab.Header, Is.EqualTo("second"), "its binding was closed by the strip it left");
        });
    }
}
