using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// Which commands one level of key tips offers, and what typing narrows it to. A SCOPE (a ribbon tab) belongs to the
// level above it but its contents do not: they are the next level down, or one letter would be claimed twice.
[TestFixture]
public class KeyTipServiceTests
{
    private static Border Tipped(string keyTip, bool isScope = false)
    {
        var element = new Border();
        KeyTipService.SetKeyTip(element, keyTip);
        if (isScope) KeyTipService.SetIsScope(element, true);
        return element;
    }

    [Test]
    public void ALevelOffersWhatWearsAKeyTip()
    {
        var root = new StackPanel();
        root.Children.Add(Tipped("H"));
        root.Children.Add(new Border());          // no key tip - not offered
        root.Children.Add(Tipped("N"));

        Assert.That(KeyTipService.Candidates(root), Has.Count.EqualTo(2));
    }

    [Test]
    public void AScopeIsOfferedButItsContentsAreNot()
    {
        var tab = Tipped("H", isScope: true);
        var inner = new StackPanel();
        inner.Children.Add(Tipped("V"));
        tab.Child = inner;

        var root = new StackPanel();
        root.Children.Add(tab);

        var candidates = KeyTipService.Candidates(root);

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.That(candidates[0], Is.SameAs(tab), "the tab belongs to this level; its commands to the next");
        });
    }

    // A tab's letters are written on the tab and worn by its header: the tab itself stands in the band its commands are
    // offered from, and wore a stray badge of its own among them.
    [Test]
    public void ATabIsNotOfferedAmongItsCommands()
    {
        var tab = new RibbonTab { Header = "Home" };
        KeyTipService.SetKeyTip(tab, "H");
        var band = new StackPanel();
        band.Children.Add(tab);
        band.Children.Add(Tipped("V"));

        var candidates = KeyTipService.Candidates(band);

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.That(KeyTipService.GetKeyTip(candidates[0]), Is.EqualTo("V"));
        });
    }

    [Test]
    public void WhatIsHiddenIsNotOffered()
    {
        var root = new StackPanel();
        var hidden = Tipped("H");
        hidden.Visibility = Visibility.Collapsed;
        root.Children.Add(hidden);

        Assert.That(KeyTipService.Candidates(root), Is.Empty);
    }

    [Test]
    public void TypingNarrowsToWhatStillStartsWithIt()
    {
        var root = new StackPanel();
        root.Children.Add(Tipped("FN"));
        root.Children.Add(Tipped("FS"));
        root.Children.Add(Tipped("H"));

        Assert.That(KeyTipService.Narrow(KeyTipService.Candidates(root), "F"), Has.Count.EqualTo(2));
    }

    [Test]
    public void AnExactHitActsEvenWhenItIsAPrefixOfAnother()
    {
        var root = new StackPanel();
        var f = Tipped("F");
        root.Children.Add(f);
        root.Children.Add(Tipped("FN"));

        var narrowed = KeyTipService.Narrow(KeyTipService.Candidates(root), "F");

        Assert.Multiple(() =>
        {
            Assert.That(narrowed, Has.Count.EqualTo(1));
            Assert.That(narrowed[0], Is.SameAs(f), "an exact match must act rather than wait for a longer one");
        });
    }
}
