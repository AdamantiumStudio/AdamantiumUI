using System;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

// The header's template is computed, as WPF's SelectionBoxItemTemplate: the item's template for an item, none for the
// placeholder, and not something to set from outside.
[TestFixture]
public class DropDownDisplayTemplateTests
{
    private static DataTemplate Template() => new(() => new TemplateResult { RootComponent = new Border() });

    [Test]
    public void AChosenItem_IsShownByTheItemTemplate()
    {
        var template = Template();
        var drop = new DropDown { ItemTemplate = template, ItemsSource = new[] { "a", "b" }, SelectedIndex = 1 };

        Assert.That(drop.DisplayContentTemplate, Is.SameAs(template));
    }

    [Test]
    public void AChosenItem_IsShownByTheTemplateTheSelectorPicks()
    {
        var picked = Template();
        var drop = new DropDown
        {
            ItemTemplateSelector = new FixedTemplateSelector(picked), ItemsSource = new[] { "a", "b" }, SelectedIndex = 0
        };

        Assert.That(drop.DisplayContentTemplate, Is.SameAs(picked));
    }

    // The placeholder is words, not an item: neither the item template nor the selector is asked to draw it.
    [Test]
    public void ThePlaceholder_HasNoTemplate()
    {
        var drop = new DropDown
        {
            ItemTemplateSelector = new FixedTemplateSelector(Template()), Placeholder = "Choose", ItemsSource = new[] { "a" }
        };

        Assert.Multiple(() =>
        {
            Assert.That(drop.DisplayContent, Is.EqualTo("Choose"));
            Assert.That(drop.DisplayContentTemplate, Is.Null);
        });
    }

    [Test]
    public void TheHeadersContentAndTemplate_CannotBeSetFromOutside()
    {
        var drop = new DropDown();

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => drop.SetValue(DropDown.DisplayContentTemplateProperty, Template()));
            Assert.Throws<InvalidOperationException>(() => drop.SetValue(DropDown.DisplayContentProperty, "x"));
        });
    }
}
