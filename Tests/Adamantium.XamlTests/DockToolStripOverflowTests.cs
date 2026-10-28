using System;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.GraphiteTheme;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A tool group's tabs overflow exactly as a document strip's do, and say so the same way: a ▾ listing every tab
/// and the tabs fading out at the edge they went past. The tool template is its own, so every theme is asked - along the
/// bottom while docked, and down the column once folded against a side.</summary>
[TestFixture]
public class DockToolStripOverflowTests
{
    private FakeApp _app;

    private static readonly Func<Theme>[] Themes = [() => new Fluent(), () => new Graphite(), () => new MacOs()];

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(Theme theme)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static PaneGroup Group(PaneGroupState state, DockZone edge, Size<double> size, int tabs = 12,
        PaneKind kind = PaneKind.Tool)
    {
        var group = new PaneGroup { Kind = kind, State = state, Edge = edge };
        for (var i = 0; i < tabs; i++)
        {
            group.Items.Add(new Pane { Header = $"Tool panel {i}" });
        }

        group.SelectedIndex = 0;
        group.ApplyCurrentTheme();
        Settle(group, size);
        return group;
    }

    private static void Settle(PaneGroup group, Size<double> size)
    {
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(group);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        for (var pass = 0; pass < 3; pass++)
        {
            group.Measure(size);
            group.Arrange(new Rect(0, 0, size.Width, size.Height));
        }
    }

    private static Visibility VisibilityOf(PaneGroup group, string part) =>
        ((UIComponent)group.GetTemplateChild(part)).Visibility;

    private static TabStripScroller Strip(PaneGroup group) => (TabStripScroller)group.GetTemplateChild("PART_TabStrip");

    // The mask's opacity at the strip's two ends: zero where tabs fade out, one where they do not.
    private static (float Back, float Forward) MaskEnds(PaneGroup group)
    {
        var mask = (LinearGradientBrush)Strip(group).OpacityMask;
        var stops = mask.GradientStops;
        return (stops[0].Color.A / 255f, stops[stops.Count - 1].Color.A / 255f);
    }

    private static PointerWheelEventArgs Notch(int delta) => new(Mouse.Pointer, InputModifiers.None, delta, 0)
    {
        RoutedEvent = PointerEvents.PointerWheelChangedEvent
    };

    [TestCaseSource(nameof(Themes))]
    public void AnOverflowingDockedStripShowsTheChevronAndFadesTheFarEdge(Func<Theme> theme)
    {
        Use(theme());
        var group = Group(PaneGroupState.Docked, DockZone.Bottom, new Size<double>(220, 300));

        Assert.That(group.CanScrollTabsForward, Is.True, "twelve tabs do not fit in 220 pixels");
        Assert.That(VisibilityOf(group, "PART_TabOverflow"), Is.EqualTo(Visibility.Visible), "the ▾");
        Assert.That(Strip(group).OpacityMask, Is.InstanceOf<LinearGradientBrush>(), "the tabs fade by a mask");
        Assert.That(MaskEnds(group), Is.EqualTo((1f, 0f)), "out at the far edge only: nothing is past the near one");
    }

    [TestCaseSource(nameof(Themes))]
    public void AStripThatFitsShowsNeitherTheChevronNorAMask(Func<Theme> theme)
    {
        Use(theme());
        var group = Group(PaneGroupState.Docked, DockZone.Bottom, new Size<double>(800, 300), tabs: 2);

        Assert.Multiple(() =>
        {
            Assert.That(VisibilityOf(group, "PART_TabOverflow"), Is.EqualTo(Visibility.Collapsed));
            Assert.That(Strip(group).OpacityMask, Is.Null, "a mask makes the strip a layer, and nothing needs fading");
        });
    }

    [TestCaseSource(nameof(Themes))]
    public void AStripPannedOffItsStartFadesBothEdges(Func<Theme> theme)
    {
        Use(theme());
        var group = Group(PaneGroupState.Docked, DockZone.Bottom, new Size<double>(220, 300));

        ((Pane)group.Items[0]).RaiseEvent(Notch(-120));
        Settle(group, new Size<double>(220, 300));

        Assert.That(group.CanScrollTabsBack, Is.True, "a notch down pans the first tab out of view");
        Assert.That(MaskEnds(group), Is.EqualTo((0f, 0f)), "tabs past both edges");
    }

    [TestCase(DockZone.Left)]
    [TestCase(DockZone.Right)]
    public void AStripFoldedDownASideFadesAlongTheColumn(DockZone edge)
    {
        foreach (var theme in Themes)
        {
            Use(theme());
            var group = Group(PaneGroupState.Collapsed, edge, new Size<double>(300, 160));
            var mask = (LinearGradientBrush)Strip(group).OpacityMask;

            Assert.Multiple(() =>
            {
                Assert.That(group.CanScrollTabsForward, Is.True, $"{theme().Name}: twelve turned tabs do not fit in 160");
                Assert.That(VisibilityOf(group, "PART_TabOverflow"), Is.EqualTo(Visibility.Visible), "the ▾");
                Assert.That(mask.EndPoint.Y, Is.GreaterThan(mask.StartPoint.Y), "the mask runs down the column");
                Assert.That(mask.EndPoint.X, Is.EqualTo(mask.StartPoint.X), "and not across it");
                Assert.That(Strip(group).RenderSize.Width, Is.LessThan(60), "the column is as wide as a turned label");
            });
        }
    }

    [TestCaseSource(nameof(Themes))]
    public void OnAFoldedStripOnlyATabRevealsThePanel(Func<Theme> theme)
    {
        Use(theme());
        var area = new DockingArea { DividerThickness = 0 };
        var center = new PaneGroup { Name = "scene", Zone = DockZone.Center };
        center.Items.Add(new Pane { Header = "scene", Id = "scene" });
        var right = new PaneGroup { Name = "inspector", Zone = DockZone.Right, Size = 240 };
        for (var i = 0; i < 12; i++)
        {
            right.Items.Add(new Pane { Header = $"Tool panel {i}", Id = $"tool{i}" });
        }

        area.Children.Add(center);
        area.Children.Add(right);
        var window = new Window { Width = 1000, Height = 400, Content = area };
        SettleWindow(window);
        var node = area.Layout.FindGroup("tool0");
        Assert.That(area.Layout.CollapseGroup(node), Is.True);
        area.Rebuild();
        SettleWindow(window);

        PressOn((IInputComponent)right.GetTemplateChild("PART_TabOverflow"));
        Assert.That(node.State, Is.EqualTo(PaneGroupState.Collapsed), $"{theme().Name}: the ▾ lists the tabs, it shows none");

        var overflow = (ToggleButton)right.GetTemplateChild("PART_TabOverflow");
        overflow.IsChecked = true;
        SettleWindow(window);
        var list = ((Popup)right.GetTemplateChild("PART_TabOverflowPopup")).FindContentChild("PART_TabOverflowList") as ListBox;
        Assert.That(list, Is.Not.Null, $"{theme().Name}: the ▾ opened its list");

        list.SelectedIndex = 5;
        Assert.Multiple(() =>
        {
            Assert.That(node.State, Is.EqualTo(PaneGroupState.Revealed), $"{theme().Name}: a tab picked from the list shows");
            Assert.That(right.SelectedIndex, Is.EqualTo(5), "and it is the one picked");
            Assert.That(overflow.IsChecked, Is.False, "and the list closes");
        });

        area.Hide(right);
        SettleWindow(window);
        Assume.That(node.State, Is.EqualTo(PaneGroupState.Collapsed));

        PressOn((Pane)right.Items[3]);
        Assert.That(node.State, Is.EqualTo(PaneGroupState.Revealed), $"{theme().Name}: a tab shows its panel");
    }

    [TestCaseSource(nameof(Themes))]
    public void APanelPutAwayIsAsWideAsItsTurnedTabs(Func<Theme> theme)
    {
        Use(theme());
        var (window, area, right) = AreaWithAToolGroup();
        var node = area.Layout.FindGroup("tool0");
        Assert.That(area.Layout.CollapseGroup(node), Is.True);
        area.Rebuild();
        for (var pass = 0; pass < 6; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        var row = (IMeasurableComponent)Strip(right).VisualParent;
        var incremental = ((IMeasurableComponent)right).DesiredSize.Width;
        row.InvalidateMeasure();
        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(incremental, Is.EqualTo(((IMeasurableComponent)right).DesiredSize.Width).Within(0.5),
                $"{theme().Name}: nothing of the flat tabs' width is left in the column");
            Assert.That(incremental, Is.LessThan(60), $"{theme().Name}: as wide as a turned label");
        });
    }

    [Test]
    public void AThemeSwapKeepsTheSelectedTabInView()
    {
        Use(new Fluent());
        var tabs = new TabControl();
        for (var i = 0; i < 30; i++)
        {
            tabs.Items.Add(new TabItem { Header = $"Tab number {i}", Content = new Adamantium.UI.Controls.Text.TextBlock { Text = $"Page {i}" } });
        }

        var window = new Window { Width = 600, Height = 300, Content = tabs };
        tabs.ApplyCurrentTheme();
        SettleWindow(window);
        tabs.SelectedIndex = 27;
        Drain(window);
        var strip = (TabStripScroller)tabs.GetTemplateChild("PART_TabStrip");
        Assert.That(SelectedTabInView(tabs, strip), Is.True, "the selected tab was panned into view");

        Use(new Graphite());
        tabs.ApplyCurrentTheme();
        Drain(window);

        var swapped = (TabStripScroller)tabs.GetTemplateChild("PART_TabStrip");
        Assert.Multiple(() =>
        {
            Assert.That(swapped, Is.Not.SameAs(strip), "the swap built a new strip");
            Assert.That(SelectedTabInView(tabs, swapped), Is.True, "and it is panned to the selected tab again");
        });
    }

    private static bool SelectedTabInView(TabControl tabs, TabStripScroller strip)
    {
        var tab = tabs.ContainerOfTab(tabs.SelectedIndex).Bounds;
        var left = tab.X + ((IUIComponent)strip.Child).Bounds.X;
        return tab.Width > 0 && left >= -0.5 && left + tab.Width <= ((IUIComponent)strip).Bounds.Width + 0.5;
    }

    private static void Drain(Window window)
    {
        for (var pass = 0; pass < 8; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }
    }

    private static (Window Window, DockingArea Area, PaneGroup Right) AreaWithAToolGroup()
    {
        var area = new DockingArea { DividerThickness = 0 };
        var center = new PaneGroup { Name = "scene", Zone = DockZone.Center };
        center.Items.Add(new Pane { Header = "scene", Id = "scene" });
        var right = new PaneGroup { Name = "inspector", Zone = DockZone.Right, Size = 240 };
        for (var i = 0; i < 12; i++)
        {
            right.Items.Add(new Pane { Header = $"Tool panel {i}", Id = $"tool{i}" });
        }

        area.Children.Add(center);
        area.Children.Add(right);
        var window = new Window { Width = 1000, Height = 400, Content = area };
        SettleWindow(window);
        return (window, area, right);
    }

    private static void SettleWindow(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
            window.Measure(new Size<double>(1000, 400));
            window.Arrange(new Rect(0, 0, 1000, 400));
        }
    }

    private static void PressOn(IInputComponent target) =>
        target.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, InputModifiers.None, 0)
        {
            RoutedEvent = PointerEvents.PreviewPointerPressedEvent
        });

    [TestCase(DockZone.Left)]
    [TestCase(DockZone.Right)]
    public void AFoldedStripsTintLiesUnderTheMaskNotInIt(DockZone edge)
    {
        foreach (var theme in Themes)
        {
            Use(theme());
            var group = Group(PaneGroupState.Collapsed, edge, new Size<double>(300, 160));
            var strip = Strip(group);
            var tint = _app.ResourceManager.FindResource(group, "DockFoldedStripFill") switch
            {
                SolidColorBrush brush => brush.Color,
                Color color => color,
                var other => throw new AssertionException($"DockFoldedStripFill is {other}")
            };
            var overflow = (ToggleButton)group.GetTemplateChild("PART_TabOverflow");

            Assert.Multiple(() =>
            {
                Assert.That((BackgroundOf((IUIComponent)strip.VisualParent) as SolidColorBrush)?.Color, Is.EqualTo(tint),
                    $"{theme().Name}: the row under the strip carries the tint");
                Assert.That(TintedInside(strip, tint), Is.Null,
                    $"{theme().Name}: inside the masked strip the tint would fade out with the tabs");
                Assert.That(FilledInside(overflow), Is.Null,
                    $"{theme().Name}: the ▾ lies on the tinted row, so a fill of its own covers or doubles the tint");
            });
        }
    }

    private static Brush BackgroundOf(IUIComponent component) => component switch
    {
        Adamantium.UI.Controls.Panels.Panel panel => panel.Background,
        Adamantium.UI.Controls.Decorators.Border border => border.Background,
        _ => null
    };

    private static IUIComponent FilledInside(IUIComponent parent)
    {
        foreach (var child in parent.VisualChildren)
        {
            if (child.Visibility != Visibility.Visible) continue;
            if (BackgroundOf(child) is SolidColorBrush { Color.A: > 0 }) return child;
            if (FilledInside(child) is { } found) return found;
        }

        return null;
    }

    private static IUIComponent TintedInside(IUIComponent parent, Color tint)
    {
        foreach (var child in parent.VisualChildren)
        {
            if (BackgroundOf(child) is SolidColorBrush brush && brush.Color == tint) return child;
            if (TintedInside(child, tint) is { } found) return found;
        }

        return null;
    }

    [TestCaseSource(nameof(Themes))]
    public void TheWheelOverAToolTabPansTheStrip(Func<Theme> theme) => WheelPans(theme, PaneKind.Tool);

    [TestCaseSource(nameof(Themes))]
    public void TheWheelOverADocumentTabPansTheStrip(Func<Theme> theme) => WheelPans(theme, PaneKind.Document);

    private void WheelPans(Func<Theme> theme, PaneKind kind)
    {
        Use(theme());
        var group = Group(PaneGroupState.Docked, DockZone.None, new Size<double>(220, 300), kind: kind);

        var args = Notch(-120);
        ((Pane)group.Items[0]).RaiseEvent(args);
        Settle(group, new Size<double>(220, 300));

        Assert.Multiple(() =>
        {
            Assert.That(args.Handled, Is.True, "the strip took the wheel");
            Assert.That(group.CanScrollTabsBack, Is.True, "a notch down pans the first tab out of view");
            Assert.That(((IUIComponent)Strip(group).Child).Bounds.X, Is.LessThan(0), "and the tabs really moved");
        });
    }
}
