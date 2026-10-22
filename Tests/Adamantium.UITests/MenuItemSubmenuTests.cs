using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A submenu stays open while the pointer travels into it: every row inside it - one written in markup as much
/// as one made from data - knows the row whose submenu it is in, and hovering it holds that submenu open.</summary>
[TestFixture]
public class MenuItemSubmenuTests
{
    private static ControlTemplate RowTemplate() => new(() =>
    {
        var popup = new Popup
        {
            ChildTemplate = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var scroll = new MenuScrollViewer { Content = presenter };
                var inner = new TemplateResult { RootComponent = new Border { Child = scroll } };
                inner.RegisterName("PART_MenuScroll", scroll);
                inner.RegisterName("PART_ItemsPresenter", presenter);
                return inner;
            })
        };

        var grid = new Grid();
        grid.Children.Add(popup);

        var result = new TemplateResult { RootComponent = grid };
        result.RegisterName("PART_SubmenuPopup", popup);
        return result;
    });

    // The submenu's rows are made when its card is laid out on the window's popup layer.
    private static void OpenSubmenu(MenuItem parent)
    {
        var window = new Window { Width = 400, Height = 300, Content = parent };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        ((Popup)parent.GetTemplateChild("PART_SubmenuPopup")).IsOpen = true;
        window.PopupLayer.UpdateLayout(new Size(400, 300));
    }

    [Test]
    public void ARowWrittenInMarkup_KnowsTheRowWhoseSubmenuItIsIn()
    {
        var parent = new MenuItem { Header = "Sets by document type", Template = RowTemplate() };
        var child = new MenuItem { Header = "Core only" };
        parent.Items.Add(child);

        OpenSubmenu(parent);

        Assert.That(child.OwnerMenu, Is.SameAs(parent),
            "without it, hovering the row cannot hold the submenu open and it closes under the pointer");
    }

    // A submenu holding no rows - a color palette - is held open by the pointer on its card, as one of rows is by them.
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void ThePointerOnTheSubmenusCard_HoldsItOpen_WhateverItHolds(bool ontoCard, bool open)
    {
        var parent = new MenuItem { Header = "Color", Template = RowTemplate() };
        parent.Items.Add(new Border { Width = 40, Height = 20 });
        OpenSubmenu(parent);
        parent.IsSubmenuOpen = true;
        var card = (IInputComponent)((Popup)parent.GetTemplateChild("PART_SubmenuPopup")).Child;

        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            Move(null, parent);
            Move(parent, ontoCard ? card : null);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (parent.IsSubmenuOpen && waited.ElapsedMilliseconds < (open ? 800 : 3000))
            {
                Thread.Sleep(10);
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }

        Assert.That(parent.IsSubmenuOpen, Is.EqualTo(open));
    }

    private static void Move(IInputComponent from, IInputComponent to) =>
        AncestorState.Transition(from, to, PointerEvents.PointerEnteredEvent, PointerEvents.PointerExitedEvent,
            evt => new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0) { RoutedEvent = evt });
}
