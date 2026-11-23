using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A number box's buttons where each placement puts them, either way round, along and down - and down the side
/// of a bar, a box as narrow as its number, its unit under it - under each theme.</summary>
[TestFixture]
public class NumericUpDownPlacementThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    // The resources first: a theme's styles look theirs up as they are built.
    private void Use(string name)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static void Show(Window window)
    {
        window.ApplyCurrentTheme();
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static Vector2<double> Middle(IUIComponent element, IUIComponent within) =>
        element.TranslatePoint(new Vector2<double>(element.RenderSize.Width / 2, element.RenderSize.Height / 2), within);

    private static readonly string[] Themes = ["Fluent", "Graphite", "MacOs"];

    // Where each button stands from the number: L(eft), R(ight), A(bove) or B(elow).
    private static readonly object[] Placements =
    [
        new object[] { NumericButtonsPlacement.Split, false, Orientation.Horizontal, "L", "R" },
        new object[] { NumericButtonsPlacement.Split, true, Orientation.Horizontal, "R", "L" },
        new object[] { NumericButtonsPlacement.Left, false, Orientation.Horizontal, "L", "L" },
        new object[] { NumericButtonsPlacement.Right, false, Orientation.Horizontal, "R", "R" },
        new object[] { NumericButtonsPlacement.Top, false, Orientation.Horizontal, "A", "A" },
        new object[] { NumericButtonsPlacement.Top, true, Orientation.Horizontal, "A", "A" },
        new object[] { NumericButtonsPlacement.Bottom, false, Orientation.Horizontal, "B", "B" },
        new object[] { NumericButtonsPlacement.Bottom, true, Orientation.Horizontal, "B", "B" },
        new object[] { NumericButtonsPlacement.Split, false, Orientation.Vertical, "A", "B" },
        new object[] { NumericButtonsPlacement.Split, true, Orientation.Vertical, "B", "A" },
        new object[] { NumericButtonsPlacement.Top, false, Orientation.Vertical, "A", "A" },
        new object[] { NumericButtonsPlacement.Top, true, Orientation.Vertical, "A", "A" },
        new object[] { NumericButtonsPlacement.Bottom, true, Orientation.Vertical, "B", "B" }
    ];

    private static System.Collections.Generic.IEnumerable<TestCaseData> Cases() =>
        from theme in Themes
        from object[] row in Placements
        select new TestCaseData(theme, row[0], row[1], row[2], row[3], row[4])
            .SetName($"{theme}_{row[0]}_{(((bool)row[1]) ? "Swapped" : "InOrder")}_{row[2]}");

    [TestCaseSource(nameof(Cases))]
    public void TheButtons_StandWhereThePlacementPutsThem(string theme, NumericButtonsPlacement placement, bool swapped,
        Orientation orientation, string decrease, string increase)
    {
        Use(theme);
        var box = new NumericUpDown
        {
            Value = 5.08, ButtonsPlacement = placement, AreButtonsSwapped = swapped, Orientation = orientation,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        var window = new Window { Width = 400, Height = 300, Content = box };
        Show(window);
        var number = (IUIComponent)box.GetTemplateChild("PART_TextBox");
        var down = (IUIComponent)box.GetTemplateChild("PART_Decrease");
        var up = (IUIComponent)box.GetTemplateChild("PART_Increase");

        Assert.Multiple(() =>
        {
            Assert.That(Side(down, number, box), Is.EqualTo(decrease), "where decrease stands from the number");
            Assert.That(Side(up, number, box), Is.EqualTo(increase), "where increase stands from the number");
            if (decrease == increase)
            {
                // A pair on one side stands side by side, decrease first unless swapped.
                Assert.That(Middle(down, box).X < Middle(up, box).X, Is.EqualTo(!swapped), "the pair's order");
            }
        });
    }

    // Above or below the number when wholly over or under it, else left or right of it.
    private static string Side(IUIComponent button, IUIComponent number, IUIComponent within)
    {
        var at = button.TranslatePoint(new Vector2<double>(0, 0), within);
        var numberAt = number.TranslatePoint(new Vector2<double>(0, 0), within);
        if (at.Y + button.RenderSize.Height <= numberAt.Y + Tolerance)
        {
            return "A";
        }

        if (at.Y >= numberAt.Y + number.RenderSize.Height - Tolerance)
        {
            return "B";
        }

        return Middle(button, within).X < Middle(number, within).X ? "L" : "R";
    }

    private const double Tolerance = 0.5;

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void TheUnit_StandsBesideTheNumber_AndUnderItDown_AndIsNothingWithoutOne(string theme)
    {
        Use(theme);
        var along = new NumericUpDown { Value = 5.08, Unit = "cm", HorizontalAlignment = HorizontalAlignment.Left };
        var down = new NumericUpDown
        {
            Value = 5.08, Unit = "cm", Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left
        };
        var none = new NumericUpDown { Value = 5.08, HorizontalAlignment = HorizontalAlignment.Left };
        var window = new Window
        {
            Width = 400, Height = 400, Content = new StackPanel { Children = { along, down, none } }
        };
        Show(window);

        var alongUnit = (TextBlock)along.GetTemplateChild("UnitText");
        var downUnit = (TextBlock)down.GetTemplateChild("UnitText");
        var alongText = (IUIComponent)along.GetTemplateChild("PART_TextBox");
        var downText = (IUIComponent)down.GetTemplateChild("PART_TextBox");

        Assert.Multiple(() =>
        {
            Assert.That(alongUnit.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(alongUnit.Text, Is.EqualTo("cm"));
            Assert.That(Side(alongUnit, alongText, along), Is.EqualTo("R"), "after the number along");
            Assert.That(Side(downUnit, downText, down), Is.EqualTo("B"), "under it down");
            Assert.That(((TextBlock)none.GetTemplateChild("UnitText")).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(down.RenderSize.Width, Is.LessThan(along.RenderSize.Width), "down, as narrow as its number");
        });
    }

    // Down the side of a bar the box stands vertically, as narrow as the column; set to stand along, it stays so, and is
    // folded as too wide.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ABoxDownTheSide_StandsVerticallyOnTheColumn_UnlessSetToStandAlong(string theme)
    {
        Use(theme);
        var narrow = new NumericUpDown { Value = 5.08 };
        var along = new NumericUpDown { Value = 3.49, Orientation = Orientation.Horizontal };
        var group = new ToolBarGroup
        {
            Header = "Size", Items = { new Button { Content = "B", Width = 30, MinWidth = 0 }, narrow, along }
        };
        var bar = new ToolBar { Header = "Picture", Items = { group } };
        var tray = new ToolBarTray { Orientation = Orientation.Vertical, Items = { bar } };
        var window = new Window
        {
            Width = 600, Height = 600,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { tray } }
        };
        Show(window);
        var narrowItem = narrow.GetVisualAncestors().OfType<ToolBarItem>().First();
        var alongItem = group.ItemContainerGenerator.ContainerFromIndex(2) as ToolBarItem;

        Assert.Multiple(() =>
        {
            Assert.That(narrow.Orientation, Is.EqualTo(Orientation.Vertical), "the column turned it");
            Assert.That(narrowItem.Form, Is.EqualTo(ToolBarItemForm.Shown));
            Assert.That(narrow.DesiredSize.Width, Is.LessThanOrEqualTo(bar.ColumnWidth), "it fits the column");
            Assert.That(along.Orientation, Is.EqualTo(Orientation.Horizontal), "what is set stands");
            Assert.That(alongItem?.Form, Is.EqualTo(ToolBarItemForm.Folded), "too wide along, so folded");
        });

        tray.Orientation = Orientation.Horizontal;
        Show(window);

        Assert.That(narrow.Orientation, Is.EqualTo(Orientation.Horizontal), "along a bar it stands along again");
    }
}
