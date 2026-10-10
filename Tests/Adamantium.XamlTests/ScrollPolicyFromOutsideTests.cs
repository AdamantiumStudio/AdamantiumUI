using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// ScrollViewer attached properties set on a list reach its themed template via {TemplateBinding
// (ScrollViewer.VerticalScrollBarVisibility)}; uses the shipped theme's template.
[TestFixture]
public class ScrollPolicyFromOutsideTests
{
    [OneTimeSetUp]
    public void EnsureAppContext() => UIAppContext.Initialize(new FakeApp(new AdamantiumDependencyContainer()), null);

    private static ControlTemplate ListTemplateOf(ITheme theme)
    {
        theme.Initialize();

        var setter = theme.MergedStyles.Styles
            .Where(s => s.Selector.Classes.Count == 0 && s.Selector.Types.Any(t => t == typeof(ListBox)))
            .SelectMany(s => s.Setters)
            .LastOrDefault(s => s.Property == "Template");

        Assert.That(setter, Is.Not.Null, "the theme gives a plain ListBox no template");
        return setter.Value as ControlTemplate;
    }

    private static ScrollViewer ViewerOf(ITheme theme, ScrollBarVisibility? vertical, out ListBox list)
    {
        var template = ListTemplateOf(theme);

        list = new ListBox();
        if (vertical.HasValue) ScrollViewer.SetVerticalScrollBarVisibility(list, vertical.Value);

        // An unresolvable binding throws HERE, which is the whole failure mode being guarded.
        var built = template.Build(list);

        return built.GetComponentByName("PART_ScrollHost") as ScrollViewer;
    }

    [TestCase(typeof(Adamantium.UI.Themes.GraphiteTheme.Graphite))]
    [TestCase(typeof(Adamantium.UI.Themes.FluentTheme.Fluent))]
    public void WhatIsSetOnTheListReachesTheViewerInsideIt(System.Type themeType)
    {
        var viewer = ViewerOf((ITheme)System.Activator.CreateInstance(themeType), ScrollBarVisibility.Hidden, out _);

        Assert.That(viewer, Is.Not.Null, "PART_ScrollHost is missing - the template did not build");
        Assert.That(viewer.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Hidden));
    }

    /// <summary>Saying nothing must leave the sensible default rather than a zero: the defaults live on the attached
    /// properties, so a list that never mentions scrolling still scrolls down and not across.</summary>
    [TestCase(typeof(Adamantium.UI.Themes.GraphiteTheme.Graphite))]
    [TestCase(typeof(Adamantium.UI.Themes.FluentTheme.Fluent))]
    public void SayingNothingLeavesTheDefaultPolicy(System.Type themeType)
    {
        var viewer = ViewerOf((ITheme)System.Activator.CreateInstance(themeType), null, out _);

        Assert.Multiple(() =>
        {
            Assert.That(viewer.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto));
            Assert.That(viewer.HorizontalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Disabled));
        });
    }

    /// <summary>It stays LIVE: changing it later moves the viewer with it, so a policy driven by a binding or a setting
    /// behaves the same as one written in markup.</summary>
    [Test]
    public void ChangingItLaterMovesTheViewerToo()
    {
        var viewer = ViewerOf(new Adamantium.UI.Themes.GraphiteTheme.Graphite(), ScrollBarVisibility.Hidden, out var list);

        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Visible);

        Assert.That(viewer.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Visible));
    }
}
