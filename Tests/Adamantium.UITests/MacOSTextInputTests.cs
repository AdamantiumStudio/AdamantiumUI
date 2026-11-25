using System;
using System.Collections.Generic;
using Adamantium.MacOS.Foundation;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Platforms.MacOS;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What the input method makes of the keys on macOS: the key goes to the application before the text it types,
/// and not at all while it works on what is composed.</summary>
[TestFixture]
public class MacOSTextInputTests
{
    private Target _target;
    private MacOSTextInput _input;

    [SetUp]
    public void Make()
    {
        _target = new Target();
        _input = new MacOSTextInput(_target);
    }

    [Test]
    public void ALetter_IsAKey_ThenItsText()
    {
        Press(() => Insert("a"));

        Assert.That(_target.Seen, Is.EqualTo(new[] { "key", "type a" }));
    }

    [Test]
    public void AnArrow_IsItsKeyAlone()
    {
        Press(_input.Command);

        Assert.That(_target.Seen, Is.EqualTo(new[] { "key" }));
    }

    [Test]
    public void AKeyTheInputMethodHasNoUseFor_IsItsKey()
    {
        Assert.That(_input.Press(() => false, () => _target.Seen.Add("key")), Is.False);
        Assert.That(_target.Seen, Is.EqualTo(new[] { "key" }));
    }

    [Test]
    public void ADeadKey_AndALetter_TypeTheAccentedOne()
    {
        Press(() => Compose("´", 1));
        Press(() => Insert("é"));

        Assert.That(_target.Seen, Is.EqualTo(new[] { "key", "Started ", "Changed ´ 1", "type é", "Ended " }));
        Assert.That(_input.IsComposing, Is.False);
    }

    [Test]
    public void AnArrowAfterADeadKey_TypesTheAccent_ThenMoves()
    {
        Press(() => Compose("´", 1));
        _target.Seen.Clear();
        Press(() =>
        {
            Insert("´");
            _input.Command();
        });

        Assert.That(_target.Seen, Is.EqualTo(new[] { "type ´", "Ended ", "key" }));
    }

    [Test]
    public void Composing_TheKeysAreTheInputMethods()
    {
        Press(() => Compose("に", 1));
        Press(() => Compose("にほ", 2));
        Press(() => { });   // an arrow among its candidates
        Press(_input.Command);
        Press(() => Insert("日本"));

        Assert.That(_target.Seen, Is.EqualTo(new[]
            { "key", "Started ", "Changed に 1", "Changed にほ 2", "type 日本", "Ended " }));
    }

    [Test]
    public void TheCompositionErased_Ends()
    {
        Press(() => Compose("に", 1));
        _target.Seen.Clear();
        Press(() => Compose("", 0));

        Assert.That(_target.Seen, Is.EqualTo(new[] { "Ended " }));
        Assert.That(_input.MarkedRange.IsFound, Is.False);
    }

    // The letter held down is in the text, and is replaced by the accent chosen for it.
    [Test]
    public void AnAccentChosen_TakesThePlaceOfTheLetterHeldDown()
    {
        Press(() => Insert("x"));
        Press(() => Insert("a"));
        Press(() => { });
        Assert.That(_input.SelectedRange, Is.EqualTo(new NSRange(2, 0)));
        _target.Seen.Clear();
        Press(() => _input.Insert("á", new NSRange(1, 1)));

        Assert.That(_target.Seen, Is.EqualTo(new[] { "erase 1", "type á" }));
        Assert.That(_input.Text, Is.EqualTo("xá"));
    }

    [Test]
    public void AnAccentShownAsItIsChosen_TakesThePlaceToo()
    {
        Press(() => Insert("a"));
        _target.Seen.Clear();
        Press(() => _input.Compose("á", new NSRange(1, 0), new NSRange(0, 1)));
        Press(() => _input.Compose("à", new NSRange(1, 0), NSRange.None));
        Press(() => Insert("à"));

        Assert.That(_target.Seen, Is.EqualTo(new[]
            { "erase 1", "Started ", "Changed á 1", "Changed à 1", "type à", "Ended " }));
    }

    [Test]
    public void AfterTheCaretMoved_NothingIsTakenBack()
    {
        Press(() => Insert("ab"));
        Press(_input.Command);
        _target.Seen.Clear();
        Press(() => _input.Insert("á", new NSRange(1, 1)));

        Assert.That(_target.Seen, Is.EqualTo(new[] { "key", "type á" }));
    }

    // From the emoji panel or dictation, no key pressed.
    [Test]
    public void TextWithNoKey_IsTypedAtOnce()
    {
        Insert("👍🏽");
        Assert.That(_target.Seen, Is.EqualTo(new[] { "type 👍🏽" }));

        _target.Seen.Clear();
        _input.Insert("🙂", new NSRange(0, 4));
        Assert.That(_target.Seen, Is.EqualTo(new[] { "erase 1", "type 🙂" }), "taken back as one character");
    }

    [Test]
    public void LeftComposing_ItIsTypedAsItIs()
    {
        Press(() => Compose("abc", 3));
        _target.Seen.Clear();
        _input.Commit();

        Assert.That(_target.Seen, Is.EqualTo(new[] { "type abc", "Ended " }));
    }

    [Test]
    public void LetGoOf_WhatIsComposedIsNotTyped()
    {
        Press(() => Compose("abc", 3));
        _target.Seen.Clear();
        _input.Discard();

        Assert.That(_target.Seen, Is.EqualTo(new[] { "Ended " }));
    }

    [Test]
    public void TheText_IsToldInParts()
    {
        Press(() => Insert("ab"));
        Press(() => Compose("cd", 1));

        Assert.Multiple(() =>
        {
            Assert.That(_input.MarkedRange, Is.EqualTo(new NSRange(2, 2)));
            Assert.That(_input.SelectedRange, Is.EqualTo(new NSRange(3, 0)));
            Assert.That(_input.Part(new NSRange(1, 2), out var part), Is.EqualTo("bc"));
            Assert.That(part, Is.EqualTo(new NSRange(1, 2)));
            Assert.That(_input.Part(new NSRange(3, 100), out part), Is.EqualTo("d"));
            Assert.That(part, Is.EqualTo(new NSRange(3, 1)));
            Assert.That(_input.Part(NSRange.None, out part), Is.Null);
            Assert.That(part.IsFound, Is.False);
            Assert.That(_input.Part(new NSRange(9, 1), out _), Is.Null);
        });
    }

    private void Press(Action made) => _input.Press(() =>
    {
        made();
        return true;
    }, () => _target.Seen.Add("key"));

    private void Insert(string text) => _input.Insert(text, NSRange.None);

    private void Compose(string text, int cursor) => _input.Compose(text, new NSRange(cursor, 0), NSRange.None);

    private sealed class Target : ITextInputTarget
    {
        public List<string> Seen { get; } = [];

        public void Type(string text) => Seen.Add($"type {text}");

        public void Compose(RawTextCompositionEventType type, string text, int cursor) =>
            Seen.Add(type == RawTextCompositionEventType.Changed ? $"{type} {text} {cursor}" : $"{type} ");

        public void Erase(int count) => Seen.Add($"erase {count}");
    }
}
