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

/// <summary>A Large command's label takes up to Ribbon.LabelLines lines (2 by default), broken where the wider line is
/// narrowest, as Office's, centered; a drop-down's arrow stands under it; Large commands stand the band's full height.
/// Laid out in the theme's own band.</summary>
[TestFixture]
public class RibbonLabelLinesThemeTests
{
    private FakeApp _app;
    private double _band;

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
        var ribbon = new Ribbon();
        ribbon.ApplyCurrentTheme();
        _band = ribbon.GroupsAreaHeight;
    }

    private RibbonGroup Laid(params IUIComponent[] commands)
    {
        var group = new RibbonGroup { Header = "Group" };
        foreach (var command in commands)
        {
            group.Items.Add(command);
        }

        group.ApplyCurrentTheme();
        Relaid(group);
        return group;
    }

    private void Relaid(RibbonGroup group)
    {
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(group);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        group.Measure(new Size<double>(1000, _band));
        group.Arrange(new Rect(0, 0, 1000, _band));
    }

    private static TextBlock Label(IUIComponent command) => command.GetVisualDescendants().OfType<TextBlock>()
        .Single(text => Shown(text, command));

    private static bool Shown(IUIComponent element, IUIComponent command)
    {
        for (var node = element; node != null && !ReferenceEquals(node, command); node = node.VisualParent)
        {
            if (node.Visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Lines(TextBlock label) => Enumerable.Range(0, label.Layout.LineCount)
        .Select(i => label.Layout.GetLine(i))
        .Select(line => label.Layout.Text[line.Start..line.End].Trim())
        .ToArray();

    private static readonly string[] Themes = ["Fluent", "Graphite", "MacOs"];

    [TestCaseSource(nameof(Themes))]
    public void ALargeLabelOfTwoWords_TakesTwoLines_AndTheCommandIsNarrower(string theme)
    {
        Use(theme);
        var wrapped = new RibbonButton { Content = "Format Painter" };
        var single = new RibbonButton { Content = "Format Painter", LabelLines = 1 };
        Laid(wrapped, single);

        Assert.Multiple(() =>
        {
            Assert.That(Lines(Label(wrapped)), Is.EqualTo(new[] { "Format", "Painter" }));
            Assert.That(Lines(Label(single)), Is.EqualTo(new[] { "Format Painter" }), "LabelLines 1 keeps one line");
            Assert.That(wrapped.DesiredSize.Width, Is.LessThan(single.DesiredSize.Width));
        });
    }

    [TestCaseSource(nameof(Themes))]
    public void AOneWordLabel_StaysOnOneLine(string theme)
    {
        Use(theme);
        var paste = new RibbonButton { Content = "Paste" };
        Laid(paste);

        Assert.That(Lines(Label(paste)), Is.EqualTo(new[] { "Paste" }));
    }

    [TestCaseSource(nameof(Themes))]
    public void AMediumLabel_IsOneLineBesideTheIcon(string theme)
    {
        Use(theme);
        var medium = new RibbonButton { Content = "Format Painter" };
        Ribbon.SetMaxSize(medium, RibbonSize.Medium);
        Laid(medium);

        Assert.That(Lines(Label(medium)), Is.EqualTo(new[] { "Format Painter" }));
    }

    private static IUIComponent Part(IUIComponent command, string name) =>
        command.GetVisualDescendants().OfType<IUIComponent>().Single(part => part.Name == name);

    // The arrow on a line of its own under the label, the label's lines centered over it.
    private static void ArrowUnderTheCenteredLabel(IUIComponent command, int lines)
    {
        var label = Label(command);
        var box = label.TransformBoundsToVisual(command);
        var arrow = Part(command, "DropDownGlyph").TransformBoundsToVisual(command);
        Assert.Multiple(() =>
        {
            Assert.That(label.Layout.LineCount, Is.EqualTo(lines));
            Assert.That(arrow.Y, Is.GreaterThanOrEqualTo(box.Bottom - 0.5), "the arrow under the label");
            Assert.That(arrow.X + arrow.Width / 2, Is.EqualTo(box.X + box.Width / 2).Within(1.5), "under its middle");
            for (var i = 0; i < label.Layout.LineCount; i++)
            {
                var line = label.Layout.GetLine(i);
                var rects = label.Layout.GetRangeRects(line.Start,
                    line.Start + label.Layout.Text[line.Start..line.End].TrimEnd().Length);
                Assert.That((rects.Min(rect => rect.Left) + rects.Max(rect => rect.Right)) / 2,
                    Is.EqualTo(label.Bounds.Width / 2).Within(1.5), $"line {i} centered");
            }
        });
    }

    [TestCaseSource(nameof(Themes))]
    public void ADropDownsArrow_StandsUnderItsCenteredLabel(string theme)
    {
        Use(theme);
        var insert = new RibbonDropDownButton { Content = "Insert Table" };
        var paste = new RibbonDropDownButton { Content = "Paste" };
        Laid(insert, paste);

        Assert.That(Lines(Label(insert)), Is.EqualTo(new[] { "Insert", "Table" }));
        ArrowUnderTheCenteredLabel(insert, 2);
        ArrowUnderTheCenteredLabel(paste, 1);
    }

    [TestCaseSource(nameof(Themes))]
    public void ALargeSplitButtonsLabel_IsByItsIcon_AndTheArrowUnderIt(string theme)
    {
        Use(theme);
        var split = new RibbonSplitButton { Content = "Paste Special" };
        Laid(split);

        Assert.That(Label(split).GetVisualAncestors().Contains(Part(split, "ActionArea")), "the label is the action's");
        ArrowUnderTheCenteredLabel(split, 2);
    }

    [TestCaseSource(nameof(Themes))]
    public void ALargeSplitButtonsDropDownPart_ReachesTheBottom_SoAllOfTheLowerHalfOpensTheMenu(string theme)
    {
        Use(theme);
        var split = new RibbonSplitButton { Content = "Paste" };
        var painter = new RibbonButton { Content = "Format Painter" };
        Laid(split, painter);

        Assert.That(Part(split, "PART_DropDownArea").TransformBoundsToVisual(split).Bottom,
            Is.EqualTo(split.Bounds.Height - split.BorderThickness.Bottom).Within(0.5));
    }

    [TestCaseSource(nameof(Themes))]
    public void SteppingDownAndBackUp_LeavesNoPartOfTheOtherSize(string theme)
    {
        Use(theme);
        var insert = new RibbonDropDownButton { Content = "Insert Table" };
        var group = Laid(insert);

        Ribbon.SetSize(insert, RibbonSize.Medium);
        Relaid(group);
        var medium = Lines(Label(insert));
        Ribbon.SetSize(insert, RibbonSize.Large);
        Relaid(group);

        Assert.That(medium, Is.EqualTo(new[] { "Insert Table" }), "one line beside the icon");
        ArrowUnderTheCenteredLabel(insert, 2);
    }

    [TestCaseSource(nameof(Themes))]
    public void ACommandWithoutTextContent_IsNamedAsBefore(string theme)
    {
        Use(theme);
        var iconOnly = new RibbonDropDownButton { ToolTip = "Insert" };
        var element = new RibbonSplitButton { Content = new TextBlock { Text = "Inner" } };
        Laid(iconOnly, element);

        Assert.Multiple(() =>
        {
            Assert.That(iconOnly.GetAutomationPeer().Name, Is.EqualTo("Insert"), "by its tool tip");
            Assert.That(element.GetAutomationPeer().Name, Is.EqualTo("Inner"), "by its content's text");
        });
    }

    [TestCaseSource(nameof(Themes))]
    public void TwoLinesOfLabel_FitTheBand(string theme)
    {
        Use(theme);
        var commands = new IUIComponent[]
        {
            new RibbonButton { Content = "Format Painter" },
            new RibbonDropDownButton { Content = "Insert Table" },
            new RibbonSplitButton { Content = "Paste Special" },
        };
        var group = Laid(commands);

        var row = group.GetVisualDescendants().OfType<RibbonGroupPanel>().Single().Bounds.Height;
        foreach (var command in commands)
        {
            Assert.That(((IMeasurableComponent)command).DesiredSize.Height, Is.LessThanOrEqualTo(row + 0.5),
                $"{command.GetType().Name}: the commands' row is {row} high");
        }
    }

    [TestCaseSource(nameof(Themes))]
    public void APlainCommandSteppingToMedium_GoesOnOneLine_AndBackToTwo(string theme)
    {
        Use(theme);
        var painter = new RibbonButton { Content = "Format Painter" };
        var group = Laid(painter);

        Ribbon.SetSize(painter, RibbonSize.Medium);
        Relaid(group);
        var medium = Lines(Label(painter));
        Ribbon.SetSize(painter, RibbonSize.Large);
        Relaid(group);

        Assert.That(medium, Is.EqualTo(new[] { "Format Painter" }));
        Assert.That(Lines(Label(painter)), Is.EqualTo(new[] { "Format", "Painter" }));
    }

    [TestCaseSource(nameof(Themes))]
    public void ASplitButtonSteppingToSmall_AndBack_IsLargeAgain(string theme)
    {
        Use(theme);
        var split = new RibbonSplitButton { Content = "Paste Special" };
        var group = Laid(split);

        Ribbon.SetSize(split, RibbonSize.Small);
        Relaid(group);
        var small = split.GetVisualDescendants().OfType<TextBlock>().Count(text => Shown(text, split));
        Ribbon.SetSize(split, RibbonSize.Large);
        Relaid(group);

        Assert.Multiple(() =>
        {
            Assert.That(small, Is.Zero, "Small is the icon alone");
            Assert.That(Part(split, "PART_DropDownArea").TransformBoundsToVisual(split).Bottom,
                Is.EqualTo(split.Bounds.Height - split.BorderThickness.Bottom).Within(0.5));
        });
        ArrowUnderTheCenteredLabel(split, 2);
    }


    [TestCaseSource(nameof(Themes))]
    public void LargeCommands_StandTheFullHeight_WithTheirIconsInOneRow(string theme)
    {
        Use(theme);
        var paste = new RibbonButton { Content = "Paste", Icon = "M0,0 L16,16" };
        var painter = new RibbonButton { Content = "Format Painter", Icon = "M0,0 L16,16" };
        var group = Laid(paste, painter);

        var icons = new[] { paste, painter }
            .Select(command => command.GetVisualDescendants().OfType<Viewbox>().First().TransformBoundsToVisual(group).Y)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(paste.Bounds.Height, Is.EqualTo(painter.Bounds.Height).Within(0.5));
            Assert.That(icons[0], Is.EqualTo(icons[1]).Within(0.5));
        });
    }
}
