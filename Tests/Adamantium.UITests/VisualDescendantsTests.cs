using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// <c>GetVisualDescendants</c> is every element below, at any depth - not only the children. It returned the children
/// alone, so the ribbon looked for its quick-access bars among the window's children and never found one in the caption:
/// Alt gave that bar no key tips.
/// </summary>
[TestFixture]
public class VisualDescendantsTests
{
    [Test]
    public void TheDescendants_AreEveryElementBelow_DepthFirst()
    {
        var leaf = new Border();
        var inner = new Border { Child = leaf };
        var sibling = new Border();
        var panel = new StackPanel();
        panel.Children.Add(inner);
        panel.Children.Add(sibling);

        Assert.That(panel.GetVisualDescendants(), Is.EqualTo(new object[] { inner, leaf, sibling }));
    }

    [Test]
    public void AQuickAccessBarInTheCaption_IsAmongTheKeyTipsFirstLevel()
    {
        var bar = new RibbonQuickAccess();
        var ribbon = new Ribbon();
        var caption = new TitleBar
        {
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = new Border { Child = bar } })
        };
        var layout = new StackPanel();
        layout.Children.Add(caption);
        layout.Children.Add(ribbon);
        var window = new Window { Width = 600, Height = 300, Content = layout };
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        Assert.That(ribbon.TopLevelRoots(), Does.Contain(bar));
    }

    [Test]
    public void AHiddenQuickAccessBar_IsNotOffered()
    {
        var bar = new RibbonQuickAccess { Placement = RibbonQuickAccessPlacement.BelowRibbon };
        var ribbon = new Ribbon();
        var layout = new StackPanel();
        layout.Children.Add(new Border { Child = bar });
        layout.Children.Add(ribbon);
        var window = new Window { Width = 600, Height = 300, Content = layout };
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);
        bar.Visibility = Visibility.Collapsed;

        Assert.That(ribbon.TopLevelRoots().OfType<RibbonQuickAccess>(), Is.Empty);
    }
}
