using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The drawer a ribbon opens in place of its band - a catalog, a page of settings. Held like the strip's
/// content, and put away the way any flyout is: a press outside it clears <c>IsDrawerOpen</c>, so the application's
/// binding hears it.</summary>
[TestFixture]
public class RibbonDrawerTests
{
    private sealed class Catalog : INotifyPropertyChanged
    {
        private bool _isOpen;

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                _isOpen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    [Test]
    public void TheDrawer_IsALogicalChildOfTheRibbon()
    {
        var catalog = new Border();
        var ribbon = new Ribbon { Drawer = catalog };

        Assert.That(ribbon.LogicalChildren, Does.Contain(catalog));
    }

    [Test]
    public void TheDrawer_SeesTheRibbonsDataContext()
    {
        var catalog = new Border();
        var ribbon = new Ribbon { Drawer = catalog };
        var document = new object();

        ribbon.DataContext = document;

        Assert.That(catalog.DataContext, Is.SameAs(document));
    }

    [Test]
    public void APressOutsideTheDrawer_PutsItAway_AndTellsTheBinding()
    {
        var catalog = new Catalog { IsOpen = true };
        var drawer = new Popup { KeepOpen = false, Child = new Border { Width = 200, Height = 100 } };
        var ribbon = new Ribbon { DataContext = catalog };
        ribbon.Template = new ControlTemplate(() =>
        {
            var grid = new Grid();
            grid.Children.Add(drawer);
            var result = new TemplateResult { RootComponent = grid };
            result.RegisterName("PART_Drawer", drawer);

            // As the themes write it - IsOpen="{TemplateBinding IsDrawerOpen}" - and as their generated code adds it.
            result.AddTemplateBinding(drawer, nameof(Popup.IsOpen), new TemplateBinding { Path = nameof(Ribbon.IsDrawerOpen) });
            return result;
        });
        ribbon.SetBinding(nameof(Ribbon.IsDrawerOpen), new Binding(nameof(Catalog.IsOpen))
        {
            Mode = BindingMode.TwoWay,
            IsImmediate = true
        });

        var outside = new Border { Width = 40, Height = 40 };
        var host = new StackPanel();
        host.Children.Add(ribbon);
        host.Children.Add(outside);
        var window = new Window { Width = 400, Height = 300, Content = host };
        Settle(window);
        Assert.That(drawer.IsOpen, Is.True, "precondition: the drawer is out");

        ((IObservableComponent)outside).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, MouseButtons.Left,
            MouseButtonState.Pressed, InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.IsDrawerOpen, Is.False);
            Assert.That(catalog.IsOpen, Is.False, "the application's binding heard it");
        });

        catalog.IsOpen = true;
        Settle(window);

        Assert.That(drawer.IsOpen, Is.True, "put away by a press, the drawer still opens again from the view model");
    }
}
