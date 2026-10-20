using System.Collections.Generic;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An editable drop-down, as Word's font and size boxes: what is typed picks the item that reads so, or runs
/// the command with a value of the items' type.</summary>
[TestFixture]
public class DropDownEditableThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(string name)
    {
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static void Lay(DropDown drop)
    {
        drop.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(drop);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        drop.Measure(new Size(200, 40));
        drop.Arrange(new Rect(0, 0, 200, 40));
    }

    private static IValueProvider Value(DropDown drop) =>
        (IValueProvider)drop.GetAutomationPeer().GetPattern(PatternId.Value);

    private static TextBox Box(DropDown drop) => (TextBox)drop.GetTemplateChild("PART_EditableTextBox");

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ANameTyped_PicksTheItemThatReadsSo_IgnoringCase(string theme)
    {
        Use(theme);
        var ran = new List<object>();
        var fonts = new DropDown
        {
            IsEditable = true, SelectedValuePath = "Content", Command = new AdamantiumCommand<object>(ran.Add),
            Items = { new DropDownItem { Content = "Arial" }, new DropDownItem { Content = "Georgia" } }
        };
        Lay(fonts);

        Value(fonts).SetValue("georgia");

        Assert.Multiple(() =>
        {
            Assert.That(Box(fonts).Visibility, Is.EqualTo(Visibility.Visible), "the box is shown");
            Assert.That(fonts.SelectedIndex, Is.EqualTo(1));
            Assert.That(ran, Is.EqualTo(new object[] { "Georgia" }));
            Assert.That(Value(fonts).Value, Is.EqualTo("Georgia"), "the box reads as the item");
            Assert.That(Value(fonts).IsReadOnly, Is.False);
        });
    }

    // A size no item has is run as a number, as the items are; what is no number is not taken.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AValueNoItemHas_RunsTheCommandAsTheItemsType(string theme)
    {
        Use(theme);
        var ran = new List<object>();
        var sizes = new DropDown
        {
            IsEditable = true, Command = new AdamantiumCommand<object>(ran.Add), Items = { 10.0, 12.0 }
        };
        Lay(sizes);

        Value(sizes).SetValue("13");
        Value(sizes).SetValue("big");
        Value(sizes).SetValue("12");

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(new object[] { 13.0, 12.0 }), "13 run as a number, 'big' not at all, 12 picked");
            Assert.That(sizes.SelectedIndex, Is.EqualTo(1));
        });
    }

    // Typed with the list open, "1" lit 10 on the way and "13" lit nothing: Enter took the 10 passed on the way.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void EnterWithTheListOpen_TakesWhatIsTyped_NotARowPassed(string theme)
    {
        Use(theme);
        var ran = new List<object>();
        var sizes = new DropDown
        {
            IsEditable = true, Command = new AdamantiumCommand<object>(ran.Add), Items = { 10.0, 11.0, 12.0 }
        };
        Lay(sizes);
        sizes.IsDropDownOpen = true;

        Box(sizes).Text = "1";
        Box(sizes).Text = "13";
        Box(sizes).RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.Enter, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent, OriginalSource = Box(sizes)
        });

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(new object[] { 13.0 }));
            Assert.That(sizes.IsDropDownOpen, Is.False);
            Assert.That(sizes.Focusable, Is.False, "the box is the stop, not the drop-down");
        });
    }

    // A space typed into the box is part of the name, not a key that opens the list.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ASpaceInTheBox_DoesNotOpenTheList(string theme)
    {
        Use(theme);
        var fonts = new DropDown { IsEditable = true, Items = { new DropDownItem { Content = "Times New Roman" } } };
        Lay(fonts);

        var space = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.Space, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent, OriginalSource = Box(fonts)
        };
        Box(fonts).RaiseEvent(space);

        Assert.That(fonts.IsDropDownOpen, Is.False);
    }
}
