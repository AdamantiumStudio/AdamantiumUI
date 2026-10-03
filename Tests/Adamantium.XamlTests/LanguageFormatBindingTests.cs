using System;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Localization;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Bindings write numbers and dates by the application's language, follow it when it changes, and keep their own
/// culture where one is stated.</summary>
[TestFixture]
public class LanguageFormatBindingTests
{
    private int _budget;

    private sealed class Reading
    {
        public double Value { get; set; } = 1234.5;

        public DateTime At { get; set; } = new(2026, 10, 3, 17, 45, 0);
    }

    [SetUp]
    public void EveryUpdateInOneFlush()
    {
        _budget = BindingUpdateQueue.MaxAppliesPerFlush;
        BindingUpdateQueue.MaxAppliesPerFlush = 0;
    }

    [TearDown]
    public void BackToBase()
    {
        Languages.Current = null;
        Languages.SetFormat("en-GB", new LanguageFormat());
        BindingUpdateQueue.MaxAppliesPerFlush = _budget;
    }

    [Test]
    public void AFormattedNumber_FollowsTheApplicationsLanguage()
    {
        var text = Bound(new Binding { Path = new PropertyPath("Value"), StringFormat = "{0:N1}" });

        Languages.Current = "ru";
        BindingUpdateQueue.Flush();
        var russian = text.Text;
        Languages.Current = "en";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(russian, Is.EqualTo("1 234,5").Or.EqualTo("1 234,5").Or.EqualTo("1 234,5"));
            Assert.That(text.Text, Is.EqualTo("1,234.5"));
        });
    }

    [Test]
    public void AStatedCulture_KeepsItsWayOnAnyLanguage()
    {
        Languages.Current = "ru";
        var text = Bound(new Binding { Path = new PropertyPath("Value"), StringFormat = "{0:F1}", Culture = "Invariant" });

        Assert.That(text.Text, Is.EqualTo("1234.5"));
    }

    [Test]
    public void ANumberShownAsText_FollowsTheLanguageToo()
    {
        Languages.Current = "ru";
        var text = Bound(new Binding { Path = new PropertyPath("Value") });

        Assert.That(text.Text, Is.EqualTo("1234,5"));
    }

    [Test]
    public void ALanguagesOwnFormat_IsWhatABindingWrites()
    {
        Languages.SetFormat("en-GB", new LanguageFormat { ShortTime = "HH:mm" });
        Languages.Current = "en-GB";
        var text = Bound(new Binding { Path = new PropertyPath("At"), StringFormat = "{0:t}" });

        Assert.That(text.Text, Is.EqualTo("17:45"));
    }

    private static TextBlock Bound(Binding binding)
    {
        binding.Source = new Reading();
        var text = new TextBlock();
        text.SetBinding("Text", binding);
        return text;
    }
}
