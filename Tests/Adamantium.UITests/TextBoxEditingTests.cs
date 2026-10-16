using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Extensions;
using Adamantium.UI.Platforms.Windows;
using Adamantium.UI.Text;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A text box's tab key, its editing commands, and its spelling checked by what checks it.</summary>
[TestFixture]
public class TextBoxEditingTests
{
    private static void Type(TextBoxBase box, string text)
        => box.RaiseEvent(new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });

    private static void Press(TextBoxBase box, Key key, InputModifiers modifiers = InputModifiers.None)
        => box.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });

    // Flags the words it is given, wherever they stand, and offers what it is told.
    private sealed class Words(params string[] wrong) : ISpellChecker
    {
        public List<string> Ignored { get; } = [];

        public List<string> Added { get; } = [];

        public bool Supports(string language) => true;

        public IReadOnlyList<TextRange> Check(string text, string language)
        {
            List<TextRange> found = [];
            foreach (var word in wrong.Except(Ignored).Except(Added))
            {
                for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0;
                     at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal))
                {
                    found.Add(new TextRange(at, word.Length));
                }
            }

            return found.OrderBy(range => range.Start).ToList();
        }

        public IReadOnlyList<string> Suggest(string word, string language) => word == "teh" ? ["the", "ten"] : [];

        public void IgnoreAll(string word, string language) => Ignored.Add(word);

        public void AddToDictionary(string word, string language) => Added.Add(word);
    }

    [Test]
    public void AcceptsTab_TabTypesATab_ShiftTabAndAReadOnlyBoxLeaveIt()
    {
        var box = new TextBox { AcceptsTab = true };
        var readOnly = new TextBox { AcceptsTab = true, IsReadOnly = true };
        var plain = new TextBox();

        Press(box, Key.Tab);
        Press(box, Key.Tab, InputModifiers.LeftShift);
        Press(readOnly, Key.Tab);
        Press(plain, Key.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("\t"));
            Assert.That(readOnly.Text, Is.Empty);
            Assert.That(plain.Text, Is.Empty);
        });
    }

    [Test]
    public void TheEditingCommands_ApplyWhenTheyCan()
    {
        var box = new TextBox { Text = "abc" };
        var password = new PasswordBox { Password = "secret" };
        password.SelectAll();

        Assert.Multiple(() =>
        {
            Assert.That(box.CopyCommand.CanExecute(), Is.False, "nothing selected");
            Assert.That(box.SelectAllCommand.CanExecute(), Is.True);
            Assert.That(password.CopyCommand.CanExecute(), Is.False, "a password is not given away");
            Assert.That(password.CutCommand.CanExecute(), Is.False);
        });

        box.SelectAllCommand.Execute();
        box.IsReadOnly = true;

        Assert.Multiple(() =>
        {
            Assert.That(box.CopyCommand.CanExecute(), Is.True);
            Assert.That(box.CutCommand.CanExecute(), Is.False, "a read-only box is not changed");
            Assert.That(box.PasteCommand.CanExecute(), Is.False);
        });
    }

    [Test]
    public void Replace_IsOneEditUndoTakesBack()
    {
        var box = new TextBox { Text = "a teh cat" };

        box.Replace(new TextRange(2, 3), "the");
        var replaced = box.Text;
        box.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.EqualTo("a the cat"));
            Assert.That(box.Text, Is.EqualTo("a teh cat"));
        });
    }

    // Checked at once, so a test reads the marks right after a change.
    private static void Checked(TextBox box, ISpellChecker checker)
    {
        var behavior = box.Behaviors.OfType<SpellCheckBehavior>().Single();
        behavior.ChecksInBackground = false;
        behavior.Checker = checker;
    }

    [Test]
    public void SpellCheck_IsOffUntilAsked_AndComesFromAnElementAbove()
    {
        var box = new TextBox { Text = "teh" };
        var panel = new StackPanel { Children = { box } };
        Assume.That(box.Behaviors.OfType<SpellCheckBehavior>(), Is.Empty);

        SpellCheck.SetIsEnabled(panel, true);
        var attached = box.Behaviors.OfType<SpellCheckBehavior>().Count();
        SpellCheck.SetIsEnabled(panel, false);

        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.EqualTo(1));
            Assert.That(box.Behaviors.OfType<SpellCheckBehavior>(), Is.Empty);
        });
    }

    [Test]
    public void SpellCheck_MarksTheMisspelledWords_AndForgetsThemWhenOff()
    {
        var box = new TextBox { Text = "a teh cat teh" };
        SpellCheck.SetIsEnabled(box, true);
        Checked(box, new Words("teh"));

        var marked = box.MisspelledRanges.ToList();
        box.Text = "the cat";
        var afterFix = box.MisspelledRanges.Count;
        box.Text = "teh";
        SpellCheck.SetIsEnabled(box, false);

        Assert.Multiple(() =>
        {
            Assert.That(marked, Is.EqualTo(new[] { new TextRange(2, 3), new TextRange(10, 3) }));
            Assert.That(afterFix, Is.Zero);
            Assert.That(box.MisspelledRanges, Is.Empty);
        });
    }

    [Test]
    public void SpellCheck_OffersWhatTheWordMayBe_AtTheTopOfTheMenu()
    {
        var box = new TextBox { Text = "a teh cat" };
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Copy" });
        box.ContextMenu = menu;
        var window = new Window { Width = 400, Height = 200, Content = box };
        WindowExtension.UpdateTree(window);
        SpellCheck.SetIsEnabled(box, true);
        var words = new Words("teh");
        Checked(box, words);
        box.CaretIndex = 3;

        menu.Open(box);
        var rows = menu.Items.OfType<MenuItem>().Select(row => row.Header as string).ToList();
        menu.Items.OfType<MenuItem>().First().Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Take(2), Is.EqualTo(new[] { "the", "ten" }));
            Assert.That(rows.Last(), Is.EqualTo("Copy"), "the box's own rows stay below");
            Assert.That(box.Text, Is.EqualTo("a the cat"));
            Assert.That(box.MisspelledRanges, Is.Empty);
        });
    }

    [Test]
    public void SpellCheck_IgnoreAllAndAddToDictionary_TakeTheWordAsRight()
    {
        var box = new TextBox { Text = "teh wrod" };
        var menu = new ContextMenu();
        box.ContextMenu = menu;
        var window = new Window { Width = 400, Height = 200, Content = box };
        WindowExtension.UpdateTree(window);
        SpellCheck.SetIsEnabled(box, true);
        var words = new Words("teh", "wrod");
        Checked(box, words);

        box.CaretIndex = 1;
        menu.Open(box);
        var ignore = menu.Items.OfType<MenuItem>().ElementAt(2);
        ignore.Invoke();
        menu.IsOpen = false;
        box.CaretIndex = 6;
        menu.Open(box);
        var add = menu.Items.OfType<MenuItem>().ElementAt(2);
        add.Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(words.Ignored, Is.EqualTo(new[] { "teh" }));
            Assert.That(words.Added, Is.EqualTo(new[] { "wrod" }));
            Assert.That(box.MisspelledRanges, Is.Empty);
        });
    }

    private static (TextBox Box, ContextMenu Menu) Menued(string text, params string[] wrong)
    {
        var box = new TextBox { Text = text };
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Copy" });
        box.ContextMenu = menu;
        WindowExtension.UpdateTree(new Window { Width = 400, Height = 200, Content = box });
        SpellCheck.SetIsEnabled(box, true);
        Checked(box, new Words(wrong));
        return (box, menu);
    }

    private static List<string> Rows(ContextMenu menu) =>
        menu.Items.Select(row => (row as MenuItem)?.Header as string ?? row.GetType().Name).ToList();

    [Test]
    public void AnOfferMadeForAWordThatMovedSince_ChangesNothing()
    {
        var (box, menu) = Menued("a teh cat", "teh");
        box.CaretIndex = 3;
        menu.Open(box);
        var offer = menu.Items.OfType<MenuItem>().First();

        box.Text = "zz" + box.Text;
        offer.Invoke();

        Assert.That(box.Text, Is.EqualTo("zza teh cat"));
    }

    [Test]
    public void TheSpellingRowsGoWhenTheMenuCloses()
    {
        var (box, menu) = Menued("a teh cat", "teh");
        box.CaretIndex = 3;
        menu.Open(box);
        var open = Rows(menu).Count;

        menu.IsOpen = false;

        Assert.Multiple(() =>
        {
            Assert.That(open, Is.GreaterThan(1));
            Assert.That(Rows(menu), Is.EqualTo(new[] { "Copy" }));
        });
    }

    [Test]
    public void NoOfferIsMadeThatTheBoxCouldNotHold_NorInAReadOnlyBox()
    {
        var (box, menu) = Menued("a teh cat", "teh");
        box.MaxLength = 9;
        box.CaretIndex = 3;
        menu.Open(box);
        var withRoom = Rows(menu);
        menu.IsOpen = false;
        box.IsReadOnly = true;
        menu.Open(box);
        var readOnly = Rows(menu);

        Assert.Multiple(() =>
        {
            Assert.That(withRoom, Does.Contain("the").And.Contain("ten"), "as long as the word");
            Assert.That(readOnly, Does.Not.Contain("the"));
        });
    }

    [Test]
    public void AMenuOpenedOnAnotherBox_IsNotThisBoxs()
    {
        var first = new TextBox { Text = "abc" };
        var second = new TextBox { Text = "xyz" };
        var menu = new ContextMenu();
        var copy = new MenuItem();
        TextBoxBase.SetEditAction(copy, TextEditAction.Copy);
        menu.Items.Add(copy);
        first.ContextMenu = menu;
        second.ContextMenu = menu;
        first.SelectAll();

        menu.Open(first);

        Assert.That(copy.Command, Is.SameAs(first.CopyCommand));
    }

    // Marks made for an older text stay on their words: shifted past an edit before them, dropped where it touches them.
    [Test]
    public void MarksForAnOlderText_FollowTheirWords()
    {
        TextRange[] marks = [new TextRange(2, 3), new TextRange(10, 3)];

        Assert.Multiple(() =>
        {
            Assert.That(SpellCheckBehavior.Moved(marks, "a teh cat teh", "xyz a teh cat teh"),
                Is.EqualTo(new[] { new TextRange(6, 3), new TextRange(14, 3) }), "typed before both");
            Assert.That(SpellCheckBehavior.Moved(marks, "a teh cat teh", "a teh dog teh"),
                Is.EqualTo(new[] { new TextRange(2, 3), new TextRange(10, 3) }), "changed between them");
            Assert.That(SpellCheckBehavior.Moved(marks, "a teh cat teh", "a tehx cat teh"),
                Is.EqualTo(new[] { new TextRange(11, 3) }), "the word typed into is checked again");
        });
    }

    [Test]
    public void PasteApplies_OnlyWithTextToPaste()
    {
        var box = new TextBox();
        Clipboard.SetText(string.Empty);
        var empty = box.PasteCommand.CanExecute();
        Clipboard.SetText("x");

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.False);
            Assert.That(box.PasteCommand.CanExecute(), Is.True);
        });
    }

    // The system's own spelling, with the English dictionary every Windows has.
    [Test]
    [SupportedOSPlatform("windows")]
    public void TheWindowsChecker_FindsAMisspelledWord_AndWhatItMayBe()
    {
        var checker = new WindowsSpellChecker();

        Assert.Multiple(() =>
        {
            Assert.That(checker.Check("a teh cat", "en-US"), Is.EqualTo(new[] { new TextRange(2, 3) }));
            Assert.That(checker.Suggest("teh", "en-US"), Does.Contain("the"));
            Assert.That(checker.Supports("en"), Is.True, "a language is found by its first subtag");
            Assert.That(checker.Check("the the cat", "en-US"), Is.Empty, "a repeated word is no misspelling");
        });
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public void TheWindowsChecker_IgnoresAWordForEveryTagOfTheSameDictionary()
    {
        var checker = new WindowsSpellChecker();
        Assume.That(checker.Check("qzxwv", "en"), Has.Count.EqualTo(1));

        checker.IgnoreAll("qzxwv", "en-US");

        Assert.That(checker.Check("qzxwv", "en"), Is.Empty);
    }
}
