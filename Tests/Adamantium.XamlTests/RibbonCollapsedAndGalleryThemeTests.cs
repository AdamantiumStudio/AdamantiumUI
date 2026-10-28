using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What every theme draws of a ribbon that has given way: a collapsed group's button reads as a command, and a
/// gallery offers to scroll only what it cannot show.</summary>
[TestFixture]
public class RibbonCollapsedAndGalleryThemeTests
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

    private static void Lay(MeasurableUIComponent control, double width, double height)
    {
        control.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        control.Measure(new Size<double>(width, height));
        control.Arrange(new Rect(0, 0, width, height));
    }

    private static RibbonGroup Collapsed(string header)
    {
        var group = new RibbonGroup { Header = header, Items = { new RibbonButton { Content = "Find" } } };
        Lay(group, 400, 100);
        group.ApplyVariant(group.Variants.Count - 1);

        // At its own width, as the band places it.
        group.Measure(new Size<double>(double.PositiveInfinity, 100));
        group.Arrange(new Rect(0, 0, group.DesiredSize.Width, 100));
        return group;
    }

    private static TextBlock LabelOf(RibbonGroup group, string header) =>
        ((IUIComponent)group.GetTemplateChild("PART_CollapsedButton")).GetVisualDescendants().OfType<TextBlock>()
        .Single(text => text.Text?.Replace('\n', ' ') == header);

    // A short label leaves the button at the theme's width; a long word widens it instead of being cut.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ACollapsedButton_IsAsWideAsItsLabelNeeds(string theme)
    {
        Use(theme);
        var shortOne = Collapsed("Find");
        var longOne = Collapsed("Редактирование");
        var shortButton = (IUIComponent)shortOne.GetTemplateChild("PART_CollapsedButton");
        var longButton = (IUIComponent)longOne.GetTemplateChild("PART_CollapsedButton");

        Assert.Multiple(() =>
        {
            Assert.That(shortButton.Bounds.Width, Is.EqualTo(shortOne.CollapsedWidth).Within(0.5));
            Assert.That(longButton.Bounds.Width, Is.GreaterThan(longOne.CollapsedWidth));
            Assert.That(LabelOf(longOne, "Редактирование").IsOverset, Is.False, "the word is not cut");
            Assert.That(longOne.WidthAt(longOne.Variants.Count - 1), Is.GreaterThanOrEqualTo(longButton.Bounds.Width),
                "and the band is told what it costs");
        });
    }

    // Two words too long for one line take two, as Office wraps a collapsed group's name.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ALongCollapsedLabel_WrapsToTwoLines(string theme)
    {
        Use(theme);
        var group = Collapsed("Стилистические наборы");
        var label = LabelOf(group, "Стилистические наборы");
        var line = LabelOf(Collapsed("Стили"), "Стили");

        Assert.Multiple(() =>
        {
            Assert.That(label.Bounds.Height, Is.GreaterThan(line.Bounds.Height * 1.5), "two lines");
            Assert.That(label.IsOverset, Is.False);
        });
    }

    // What a variant costs does not depend on the variant drawn when it is asked: a gallery probed at its large size
    // from its compact one answered the compact width, and the band flipped between the two on every pass.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AVariantCostsTheSame_WhicheverIsDrawn(string theme)
    {
        Use(theme);
        // As a gallery of styles is: cells from a template, their names cut short when narrow, grouped under titles.
        var gallery = new RibbonGallery
        {
            Columns = 5, CompactColumns = 3, Rows = 2, GroupMemberPath = nameof(Style.Kind),
            ItemTemplate = new Adamantium.UI.Core.Templates.DataTemplate(() =>
            {
                var name = new TextBlock { TextTrimming = Adamantium.Graphics.Fonts.TextTrimming.CharEllipses };
                name.SetBinding(nameof(TextBlock.Text), new Adamantium.UI.Core.Data.Binding(nameof(Style.Id)));
                return new Adamantium.UI.Core.Templates.TemplateResult
                {
                    RootComponent = new StackPanel { Orientation = Orientation.Horizontal, Children = { name } }
                };
            }),
            ItemsSource = Enumerable.Range(0, 9).Select(i => new Style($"Heading {i}", i < 7 ? "Paragraph" : "Character"))
                .ToList()
        };
        var group = new RibbonGroup
        {
            Header = "Styles",
            Items = { gallery, new RibbonButton { Content = "Update style to match selection" } }
        };
        var row = new Adamantium.UI.Controls.Panels.RibbonGroupsPanel { Children = { group } };
        Lay(row, 2000, 100);

        var count = group.Variants.Count - 1;
        var fromWhole = Enumerable.Range(0, count).Select(group.WidthAt).ToArray();
        Lay(row, fromWhole[1] + 1, 100);
        Adamantium.UI.Core.LayoutManager.For(row).ExecuteLayoutPass();     // settled, as a running app's frame leaves it
        Assert.That(group.CurrentVariant, Is.GreaterThan(0), "precondition: drawn smaller");
        group.ItemsHostPanel.InvalidateMeasure();    // nothing cached: asked afresh
        var fromSmaller = Enumerable.Range(0, count).Select(i => group.WidthAt(i)).ToArray();

        Assert.That(fromSmaller, Is.EqualTo(fromWhole).Within(0.5));
    }

    // A command drawn small hides its label through its template; asked for a roomier variant from a small one, a group
    // answered the small width - its label's part was switched but not measured - and the band flipped between the two
    // answers on every pass and never went idle.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AGroupOfCommands_CostsTheSameWhicheverIsDrawn(string theme)
    {
        Use(theme);
        var group = new RibbonGroup
        {
            Header = "Clipboard",
            Items =
            {
                new RibbonSplitButton { Content = "Paste" },
                new RibbonButton { Content = "Cut", MaxSize = RibbonSize.Medium },
                new RibbonButton { Content = "Copy", MaxSize = RibbonSize.Medium },
                new RibbonButton { Content = "Duplicate", MaxSize = RibbonSize.Medium }
            }
        };
        var row = new RibbonGroupsPanel { Children = { group } };
        Lay(row, 2000, 100);

        var count = group.Variants.Count - 1;
        var fromWhole = Enumerable.Range(0, count).Select(group.WidthAt).ToArray();
        Lay(row, fromWhole[count - 1] + 1, 100);
        Adamantium.UI.Core.LayoutManager.For(row).ExecuteLayoutPass();
        Assert.That(group.CurrentVariant, Is.EqualTo(count - 1), "precondition: drawn at its smallest open variant");
        group.ItemsHostPanel.InvalidateMeasure();    // nothing cached: asked afresh
        var fromSmallest = Enumerable.Range(0, count).Select(i => group.WidthAt(i)).ToArray();

        Assert.That(fromSmallest, Is.EqualTo(fromWhole).Within(0.5));
    }

    // A gallery is never cut to its bare chevron by default - it says nothing of what it opens; past its compact size the
    // group collapses into its labeled button, as Office's does.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AGalleryIsNotCutToItsChevron_TheGroupCollapsesInstead(string theme)
    {
        Use(theme);
        var gallery = new RibbonGallery { Columns = 5, CompactColumns = 3, Rows = 2 };
        for (var i = 0; i < 9; i++) gallery.Items.Add($"Heading {i}");
        var group = new RibbonGroup
        {
            Header = "Styles", Items = { gallery, new RibbonButton { Content = "Update style to match selection" } }
        };
        var row = new RibbonGroupsPanel { Children = { group } };

        foreach (var width in new double[] { 2000, 500, 300, 150, 80 })
        {
            Lay(row, width, 100);
            Assert.That(Ribbon.GetSize(gallery), Is.Not.EqualTo(RibbonSize.Small), $"at {width}");
        }

        Assert.That(group.IsCollapsed, Is.True);
    }

    // A gallery owns its column at every size it shows cells at: stacked with the command after it, that command was
    // pushed out of the bottom of the band.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ACommandAfterAGallery_StandsBesideIt(string theme)
    {
        Use(theme);
        var gallery = new RibbonGallery { Columns = 5, CompactColumns = 3, Rows = 2 };
        for (var i = 0; i < 9; i++) gallery.Items.Add($"Heading {i}");
        var update = new RibbonButton { Content = "Update style to match selection" };
        var group = new RibbonGroup { Header = "Styles", Items = { gallery, update } };
        var row = new RibbonGroupsPanel { Children = { group } };
        Lay(row, 2000, 100);

        for (var variant = 0; variant < group.Variants.Count - 1; variant++)
        {
            group.ApplyVariant(variant);
            Lay(row, group.WidthAt(variant), 100);
            if (Ribbon.GetSize(gallery) == RibbonSize.Small) continue;

            var galleryBox = gallery.TransformBoundsToVisual(row);
            var updateBox = update.TransformBoundsToVisual(row);
            Assert.That(updateBox.X, Is.GreaterThanOrEqualTo(galleryBox.X + galleryBox.Width - 0.5),
                $"at variant {variant} ({Ribbon.GetSize(gallery)} gallery, {Ribbon.GetSize(update)} command)");
        }
    }

    // A group whose caption alone changes - another language, its commands icons only - is as wide as it now measures,
    // and the next group starts after it: what the commands cost had not changed, so the width kept was the old one.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AGroupWhoseCaptionChanges_CostsItsNewWidth(string theme)
    {
        Use(theme);
        var first = new RibbonGroup { Header = "Go", Items = { new RibbonButton { Content = "Go" } } };
        var next = new RibbonGroup { Header = "Next", Items = { new RibbonButton { Content = "Next" } } };
        var row = new RibbonGroupsPanel { Children = { first, next } };
        Lay(row, 2000, 100);

        first.Header = "A caption far wider than its one command";
        Lay(row, 2000, 100);
        first.Measure(new Size<double>(double.PositiveInfinity, 100));

        Assert.Multiple(() =>
        {
            Assert.That(first.WidthAt(0), Is.EqualTo(first.DesiredSize.Width).Within(0.5));
            Assert.That(next.Bounds.X, Is.GreaterThanOrEqualTo(first.DesiredSize.Width - 0.5), "the next group starts after it");
        });
    }

    public sealed record Style(string Id, string Kind);

    // Arrows that can scroll nothing are not shown; the chevron to the whole set stays.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AGalleryShowsItsArrowsOnlyWithRowsOutOfSight(string theme)
    {
        Use(theme);
        var few = new RibbonGallery { Columns = 4, Rows = 2 };
        foreach (var item in new[] { "Normal", "Title" }) few.Items.Add(item);
        var many = new RibbonGallery { Columns = 2, Rows = 2 };
        for (var i = 0; i < 12; i++) many.Items.Add($"Style {i}");
        Lay(few, 400, 90);
        Lay(many, 400, 90);

        IUIComponent Part(RibbonGallery gallery, string name) => (IUIComponent)gallery.GetTemplateChild(name);

        Assert.Multiple(() =>
        {
            Assert.That(Part(few, "PART_ScrollUp").Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Part(few, "PART_ScrollDown").Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Part(few, "PART_More").Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Part(many, "PART_ScrollUp").Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Part(many, "PART_ScrollDown").Visibility, Is.EqualTo(Visibility.Visible));
        });
    }
}
