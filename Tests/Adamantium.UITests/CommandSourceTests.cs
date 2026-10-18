using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Buttons, menu rows, segments and galleries with a routed command: they ask its target and show what it says - can it run, is it
/// on, its key - and run it there; menus, ribbons and popups are focus scopes, so the target stays the field outside.</summary>
[TestFixture]
public class CommandSourceTests
{
    private static readonly RoutedCommand Bold = new("Bold", typeof(CommandSourceTests),
        new KeyGesture(Key.B, ModifierKeys.Control));

    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    [Test]
    public void MenusRibbonsAndPopups_AreFocusScopes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.GetIsFocusScope(new ContextMenu()), Is.True);
            Assert.That(FocusManager.GetIsFocusScope(new Ribbon()), Is.True);
            Assert.That(FocusManager.GetIsFocusScope(new Popup()), Is.True);
            Assert.That(FocusManager.GetIsFocusScope(new StackPanel()), Is.False);
        });
    }

    // A popup's content is shown on the window's overlay, not under the popup: it is still inside the popup's scope.
    [Test]
    public void ACommandFromAPopupsContent_GoesToTheFieldWithTheFocus()
    {
        var (field, editor) = Editor();
        var row = new Button { Focusable = true };
        var popup = new Popup { Child = new StackPanel { Children = { row } } };
        _ = new StackPanel { Children = { editor, popup } };
        var ran = 0;
        CommandManager.GetCommandBindings(editor).Add(new CommandBinding(Bold, (_, _) => ran++));
        FocusManager.Focus(field);

        FocusManager.Focus(row);
        CommandManager.ExecuteFrom(Bold, null, row, null);

        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.GetFocusScope(row), Is.SameAs(popup));
            Assert.That(ran, Is.EqualTo(1));
        });
    }

    [Test]
    public void AToggleOnARibbon_FollowsTheFieldOutside_AndRunsTheCommandThere()
    {
        var (field, editor) = Editor();
        var on = false;
        CommandManager.GetCommandBindings(editor).Add(new CommandBinding(Bold, (_, _) => on = !on, (_, e) =>
        {
            e.CanExecute = true;
            e.IsChecked = on;
        }));
        var toggle = new ToggleButton { Command = Bold };
        var ribbon = new StackPanel { Children = { toggle } };
        FocusManager.SetIsFocusScope(ribbon, true);
        Shown(editor, ribbon);
        FocusManager.Focus(field);
        Requery();
        var before = (toggle.IsEnabled, toggle.IsChecked);

        FocusManager.Focus(toggle);
        toggle.PerformClick();
        Requery();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo((true, (bool?)false)));
            Assert.That(on, Is.True);
            Assert.That(toggle.IsChecked, Is.True);
        });
    }

    [Test]
    public void AButtonWhoseCommandNoOneHandles_IsDisabled()
    {
        var (field, _) = Editor();
        var button = new Button { Command = Bold, CommandTarget = field };

        Assert.That(button.IsEnabled, Is.False);
    }

    [Test]
    public void AMenuRow_ShowsItsCommandsKey_UnlessItSaysItsOwn()
    {
        var row = new MenuItem { Command = Bold };
        var own = new MenuItem { InputGestureText = "Ctrl+Shift+B", Command = Bold };

        Assert.That((row.InputGestureText, own.InputGestureText), Is.EqualTo(("Ctrl+B", "Ctrl+Shift+B")));
    }

    [Test]
    public void AMenuRowClicked_RunsTheCommandOnTheFieldTheMenuWasOpenedOver()
    {
        var (field, editor) = Editor();
        var ran = new List<object>();
        CommandManager.GetCommandBindings(editor).Add(new CommandBinding(Bold, (s, _) => ran.Add(s)));
        var row = new MenuItem { Command = Bold };
        var menu = new StackPanel { Children = { row } };
        FocusManager.SetIsFocusScope(menu, true);
        Shown(editor, menu);
        FocusManager.Focus(field);
        Requery();

        FocusManager.Focus(row);
        row.Invoke();

        Assert.That(ran, Is.EqualTo(new object[] { editor }));
    }

    // No focus anywhere: the route starts from the segment, so the binding of the view it sits in takes the command.
    [Test]
    public void ASegmentWithARoutedCommand_AsksAndRunsItFromItself()
    {
        var ran = new List<object>();
        var day = new SegmentedControlItem { Content = "Day", Command = Bold, CommandParameter = "day" };
        var view = new StackPanel { Children = { day } };
        CommandManager.GetCommandBindings(view).Add(new CommandBinding(Bold, (_, e) => ran.Add(e.Parameter)));
        Shown(view);
        Requery();
        var enabled = day.IsEnabled;

        day.RunCommand();

        Assert.Multiple(() =>
        {
            Assert.That(enabled, Is.True);
            Assert.That(ran, Is.EqualTo(new object[] { "day" }));
        });
    }

    [Test]
    public void AGalleryWithARoutedItemCommand_AsksAndRunsItFromItself()
    {
        var ran = new List<object>();
        var allowed = "plane";
        var gallery = new RibbonGallery { ItemCommand = Bold };
        gallery.Items.Add("plane");
        gallery.Items.Add("cube");
        var plane = (RibbonGalleryItem)gallery.ItemContainerGenerator.Realize(0);
        var cube = (RibbonGalleryItem)gallery.ItemContainerGenerator.Realize(1);
        var view = new StackPanel { Children = { gallery } };
        CommandManager.GetCommandBindings(view).Add(new CommandBinding(Bold, (_, e) => ran.Add(e.Parameter),
            (_, e) => e.CanExecute = (string)e.Parameter == allowed));
        var outOfTheTree = (plane.IsEnabled, cube.IsEnabled);
        Shown(view);
        Requery();
        var shown = (plane.IsEnabled, cube.IsEnabled);
        gallery.PickFromContainer(plane);
        gallery.PickFromContainer(cube);

        allowed = "cube";
        CommandManager.InvalidateRequerySuggested();
        Requery();

        Assert.Multiple(() =>
        {
            Assert.That(outOfTheTree, Is.EqualTo((false, false)), "out of a tree the route reaches no binding");
            Assert.That(shown, Is.EqualTo((true, false)));
            Assert.That(ran, Is.EqualTo(new object[] { "plane" }));
            Assert.That((plane.IsEnabled, cube.IsEnabled), Is.EqualTo((false, true)));
        });
    }

    // A font box: it shows what the text has - a size it lists, one it does not, none when mixed - and a pick sets it.
    [Test]
    public void ADropDownWithARoutedCommand_ShowsItsValue_AndAPickRunsIt()
    {
        object size = 12.0;
        var ran = new List<object>();
        var box = new DropDown { Command = Bold, Placeholder = "mixed" };
        box.Items.Add(12.0);
        box.Items.Add(14.0);
        var view = new StackPanel { Children = { box } };
        CommandManager.GetCommandBindings(view).Add(new CommandBinding(Bold, (_, e) => ran.Add(e.Parameter), (_, e) =>
        {
            e.CanExecute = true;
            e.Value = size;
        }));
        Shown(view);
        Requery();
        var listed = (box.SelectedValue, box.DisplayContent);

        size = 10.5;
        CommandManager.InvalidateRequerySuggested();
        Requery();
        var unlisted = (box.SelectedItem, box.DisplayContent);
        size = null;
        CommandManager.InvalidateRequerySuggested();
        Requery();
        var mixed = box.DisplayContent;
        box.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(listed, Is.EqualTo(((object)12.0, (object)12.0)));
            Assert.That(unlisted, Is.EqualTo(((object)null, (object)10.5)));
            Assert.That(mixed, Is.EqualTo("mixed"));
            Assert.That(ran, Is.EqualTo(new object[] { 14.0 }), "showing the answer runs nothing");
        });
    }

    // A click on a ribbon's command lands the focus on the button; once the command has run, it is back in the field.
    [TestCase(true, true, ExpectedResult = "field")]
    [TestCase(false, true, ExpectedResult = "button")]
    [TestCase(true, false, ExpectedResult = "button")]
    public string ACommandFromAScopeThatReturnsFocus_GivesItBackToTheField(bool returnsFocus, bool runs)
    {
        var (field, editor) = Editor();
        var can = true;
        CommandManager.GetCommandBindings(editor).Add(new CommandBinding(Bold, (_, _) => { }, (_, e) => e.CanExecute = can));
        var button = new Button { Command = Bold };
        var ribbon = new StackPanel { Children = { button } };
        FocusManager.SetIsFocusScope(ribbon, true);
        FocusManager.SetReturnsFocus(ribbon, returnsFocus);
        Shown(editor, ribbon);
        FocusManager.Focus(field);
        Requery();

        FocusManager.Focus(button);
        can = runs;
        CommandManager.ExecuteFrom(Bold, null, button, null);

        return ReferenceEquals(FocusManager.Focused, field) ? "field" : ReferenceEquals(FocusManager.Focused, button) ? "button" : "other";
    }

    // A toggle with no command acts on its own - the panel of an application's view switches, a page shows - and gives
    // the focus back just the same.
    [TestCase(true, ExpectedResult = "field")]
    [TestCase(false, ExpectedResult = "toggle")]
    public string AToggleWithNoCommand_GivesTheFocusBackFromAScopeThatReturnsIt(bool returnsFocus)
    {
        var (field, editor) = Editor();
        var toggle = new ToggleButton();
        var bar = new StackPanel { Children = { toggle } };
        FocusManager.SetIsFocusScope(bar, true);
        FocusManager.SetReturnsFocus(bar, returnsFocus);
        Shown(editor, bar);
        FocusManager.Focus(field);

        FocusManager.Focus(toggle);
        CommandManager.ExecuteFrom(toggle.Command, null, toggle, null);

        return ReferenceEquals(FocusManager.Focused, field) ? "field" : ReferenceEquals(FocusManager.Focused, toggle) ? "toggle" : "other";
    }

    // A bar inside the ribbon gives the focus back past the ribbon too; a command that moves it keeps it where it put it.
    [Test]
    public void TheFocusGoesBack_PastANestedScope_ButNotOverWhereTheCommandPutIt()
    {
        var (field, editor) = Editor();
        var search = new Button();
        var moves = false;
        CommandManager.GetCommandBindings(editor).Add(new CommandBinding(Bold, (_, _) =>
        {
            if (moves)
            {
                FocusManager.Focus(search);
            }
        }, (_, e) => e.CanExecute = true));
        var save = new Button { Command = Bold };
        var bar = new StackPanel { Children = { save } };
        var ribbon = new StackPanel { Children = { bar, search } };
        foreach (var scope in new[] { bar, ribbon })
        {
            FocusManager.SetIsFocusScope(scope, true);
            FocusManager.SetReturnsFocus(scope, true);
        }

        Shown(editor, ribbon);
        FocusManager.Focus(field);
        Requery();
        FocusManager.Focus(save);
        CommandManager.ExecuteFrom(Bold, null, save, null);
        var past = FocusManager.Focused;

        moves = true;
        FocusManager.Focus(save);
        CommandManager.ExecuteFrom(Bold, null, save, null);

        Assert.Multiple(() =>
        {
            Assert.That(past, Is.SameAs(field));
            Assert.That(FocusManager.Focused, Is.SameAs(search));
        });
    }

    [Test]
    public void ARibbon_AndItsQuickAccessBar_ReturnTheFocus()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.GetReturnsFocus(new Ribbon()), Is.True);
            Assert.That(FocusManager.GetReturnsFocus(new RibbonQuickAccess()), Is.True);
            Assert.That(FocusManager.GetReturnsFocus(new RibbonApplicationMenu()), Is.False, "the backstage keeps it");
            Assert.That(FocusManager.GetReturnsFocus(new Popup()), Is.False);
        });
    }

    private static void Shown(params IMeasurableComponent[] parts)
    {
        var root = new StackPanel();
        foreach (var part in parts)
        {
            root.Children.Add(part);
        }

        var window = new Window { Width = 400, Height = 300, Content = root };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        Assert.That(((IUIComponent)root).IsAttachedToVisualTree, Is.True, "shown in a window");
    }

    private static (Button Field, StackPanel Editor) Editor()
    {
        var field = new Button();
        return (field, new StackPanel { Children = { field } });
    }

    private static void Requery()
    {
        AnimationManager.Tick(0.016);
    }
}
