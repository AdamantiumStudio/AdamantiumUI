using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A palette of colors as Word's font color menu holds it, and a row of ribbon commands, in every theme.</summary>
[TestFixture]
public class ColorPaletteTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [TearDown]
    public void StopEverything() => Adamantium.UI.Core.Media.Animation.AnimationManager.Reset();

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

    private static Theme ThemeNamed(string name) => name switch
    {
        "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
        "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
        _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
    };

    private static void Lay(MeasurableUIComponent component)
    {
        component.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(component);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        component.Measure(new Size(600, 400));
        component.Arrange(new Rect(0, 0, 600, 400));
    }

    private static IEnumerable<IUIComponent> Below(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            yield return child;
            foreach (var deeper in Below(child)) yield return deeper;
        }
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void APick_RunsTheCommandWithTheColor_KeepsIt_AndNoColorRunsNull(string theme)
    {
        Use(ThemeNamed(theme));
        var ran = new List<object>();
        var palette = new ColorPalette { NoColorText = "Automatic", Command = new AdamantiumCommand<object>(ran.Add) };
        Lay(palette);
        var swatches = Below(palette).OfType<ColorPaletteSwatch>().ToList();
        var red = swatches.First(s => s.Color == Color.FromRgba(255, 0, 0));
        var noColor = (Button)palette.GetTemplateChild("PART_NoColor");

        red.PerformClick();
        var kept = palette.SelectedColor;
        noColor.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(swatches, Has.Count.EqualTo(ColorPalette.StandardColors.Count));
            Assert.That(ran, Is.EqualTo(new object[] { Color.FromRgba(255, 0, 0), null }));
            Assert.That(kept, Is.EqualTo(Color.FromRgba(255, 0, 0)));
            Assert.That(AutomationProperties.GetName(red), Is.EqualTo("#FF0000"));
        });
    }

    [Test]
    public void MoreColors_ShowsThePicker_AndApplyTakesItsColor()
    {
        Use(ThemeNamed("Fluent"));
        var ran = new List<object>();
        var palette = new ColorPalette { Command = new AdamantiumCommand<object>(ran.Add) };
        Lay(palette);
        var picker = (ColorPicker)palette.GetTemplateChild("PART_Picker");
        var noColor = (UIComponent)palette.GetTemplateChild("PART_NoColor");

        ((Button)palette.GetTemplateChild("PART_MoreColors")).PerformClick();
        var open = palette.IsMoreColorsOpen;
        picker.SelectedColor = Color.FromRgba(10, 20, 30);
        ((Button)palette.GetTemplateChild("PART_Apply")).PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(open, Is.True);
            Assert.That(ran, Is.EqualTo(new object[] { Color.FromRgba(10, 20, 30) }));
            Assert.That(palette.IsMoreColorsOpen, Is.False, "the pick closes it");
            Assert.That(noColor.Visibility, Is.EqualTo(Visibility.Collapsed), "no words, no choice of no color");
        });
    }

    [Test]
    public void ARowOfRibbonCommands_DrawsThemSmall_AndStandsAsOneRow()
    {
        var bold = new RibbonToggleButton { Content = "Bold" };
        var row = new RibbonControlGroup { Children = { bold, new RibbonButton { Content = "Italic" } } };

        Assert.Multiple(() =>
        {
            Assert.That(Ribbon.GetSize(bold), Is.EqualTo(RibbonSize.Small));
            Assert.That(Ribbon.GetMaxSize(row), Is.EqualTo(RibbonSize.Medium), "a row, never a column of its own");
            Assert.That(row.GetAutomationPeer()?.ControlType, Is.EqualTo(AutomationControlType.Group));
        });
    }
}
