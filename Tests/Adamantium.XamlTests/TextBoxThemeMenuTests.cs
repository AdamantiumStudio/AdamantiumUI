using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Text;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What the theme gives a text box: an editing menu of its own whose rows run the box's commands, and a height
/// in lines.</summary>
[TestFixture]
public class TextBoxThemeMenuTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void UseFluent()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private VirtualWindow Shown(UIComponent content)
    {
        var window = new VirtualWindow { Content = content, UseCustomChrome = false, ClientWidth = 500, ClientHeight = 600 };
        window.AttachContextAndInitialize(_app.UIContext);
        for (var i = 0; i < 5; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        return window;
    }

    [Test]
    public void EachBoxHasAnEditingMenuOfItsOwn_WhoseRowsRunItsCommands()
    {
        UseFluent();
        var first = new TextBox();
        var second = new TextBox();
        var window = Shown(new StackPanel { Children = { first, second } });
        first.ContextMenu.Open(first);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var rows = first.ContextMenu.Items.OfType<MenuItem>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(first.ContextMenu, Is.Not.Null);
            Assert.That(first.ContextMenu, Is.Not.SameAs(second.ContextMenu), "one menu points at one box");
            Assert.That(rows.Select(row => row.Command), Is.EqualTo(new object[]
            {
                ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste, ApplicationCommands.SelectAll,
            }));
            Assert.That(first.ContextMenu.Items.OfType<Separator>().Count(), Is.EqualTo(1));
        });
    }

    private sealed class Words : ISpellChecker
    {
        public bool Supports(string language) => true;

        public IReadOnlyList<TextRange> Check(string text, string language) =>
            text.StartsWith("Thsi", StringComparison.Ordinal) ? [new TextRange(0, 4)] : [];

        public IReadOnlyList<string> Suggest(string word, string language) => ["This"];

        public void IgnoreAll(string word, string language)
        {
        }

        public void AddToDictionary(string word, string language)
        {
        }
    }

    // The menu key opens the menu where the keyboard is: the word at the caret is the one offered for.
    [Test]
    public void TheMenuKey_OffersForTheWordAtTheCaret_AboveTheEditingRows()
    {
        UseFluent();
        var box = new TextBox { Text = "Thsi is" };
        SpellCheck.SetIsEnabled(box, true);
        var behavior = box.Behaviors.OfType<SpellCheckBehavior>().Single();
        behavior.ChecksInBackground = false;
        behavior.Checker = new Words();
        Shown(new StackPanel { Children = { box } });
        box.CaretIndex = 1;

        box.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.Apps, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });
        var rows = box.ContextMenu.Items.Select(row => (row as MenuItem)?.Header as string ?? row.GetType().Name).ToList();

        Assert.That(rows.First(), Is.EqualTo("This"));
        Assert.That(rows.Skip(rows.IndexOf("Separator") + 1).First(), Is.EqualTo("Cut"));
    }

    // A floating placeholder given later takes its strip above the lines, as one given from the start does.
    [Test]
    public void APlaceholderGivenLater_TakesItsRoomAboveTheLines()
    {
        UseFluent();
        var early = new TextBox { MinLines = 3, FloatingPlaceholder = true, Placeholder = "Name" };
        var late = new TextBox { MinLines = 3, FloatingPlaceholder = true };
        var window = Shown(new StackPanel { Children = { early, late } });

        late.Placeholder = "Name";
        for (var i = 0; i < 3; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        Assert.That(late.DesiredSize.Height, Is.EqualTo(early.DesiredSize.Height).Within(0.5));
    }

    [Test]
    public void ABoxIsAsTallAsItsLeastLines_AndNoTallerThanItsMost()
    {
        UseFluent();
        var one = new TextBox { AcceptsReturn = true, Text = "a" };
        var three = new TextBox { AcceptsReturn = true, Text = "a", MinLines = 3 };
        var two = new TextBox
        {
            AcceptsReturn = true, MaxLines = 2, Text = string.Join("\n", Enumerable.Range(0, 10).Select(i => $"line {i}")),
        };
        var twoLines = new TextBox { AcceptsReturn = true, Text = "a\nb" };
        Shown(new StackPanel { Children = { one, three, two, twoLines } });
        var line = twoLines.DesiredSize.Height - one.DesiredSize.Height;

        Assume.That(line, Is.GreaterThan(5));
        Assert.Multiple(() =>
        {
            Assert.That(three.DesiredSize.Height - one.DesiredSize.Height, Is.EqualTo(2 * line).Within(1));
            Assert.That(two.DesiredSize.Height, Is.EqualTo(twoLines.DesiredSize.Height).Within(1));
        });
    }
}
