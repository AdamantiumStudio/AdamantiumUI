using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The InfoBar under each theme: each severity has its own ground and icon, what it has nothing for takes no
/// room, its close button closes it and says so, and closed it takes no room at all.</summary>
[TestFixture]
public class InfoBarThemeTests
{
    private FakeApp _app;

    public static IEnumerable<TestCaseData> Themes()
    {
        yield return new TestCaseData((System.Func<Theme>)(() => new Adamantium.UI.Themes.FluentTheme.Fluent()))
            .SetArgDisplayNames("Fluent");
        yield return new TestCaseData((System.Func<Theme>)(() => new Adamantium.UI.Themes.GraphiteTheme.Graphite()))
            .SetArgDisplayNames("Graphite");
        yield return new TestCaseData((System.Func<Theme>)(() => new Adamantium.UI.Themes.MacOsTheme.MacOs()))
            .SetArgDisplayNames("MacOs");
    }

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    // The theme is made AFTER the application's resources: it keeps the resource manager current when it is made, and
    // one made before would look its icons up in the last test's.
    private void Use(System.Func<Theme> made)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var theme = made();
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static InfoBar Laid(InfoBar bar)
    {
        bar.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(bar);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        bar.Measure(new Size<double>(500, 400));
        bar.Arrange(new Rect(0, 0, 500, 400));
        return bar;
    }

    private static UIComponent Part(InfoBar bar, string name) => (UIComponent)bar.GetTemplateChild(name);

    [TestCaseSource(nameof(Themes))]
    public void EachSeverity_HasItsOwnGroundAndIcon(System.Func<Theme> theme)
    {
        Use(theme);
        var grounds = new List<object>();
        var icons = new List<object>();
        foreach (var severity in new[]
                 {
                     InfoBarSeverity.Informational, InfoBarSeverity.Success, InfoBarSeverity.Warning,
                     InfoBarSeverity.Error,
                 })
        {
            var bar = Laid(new InfoBar { Severity = severity, Title = "Physics" });
            grounds.Add(bar.Background?.ToString());
            icons.Add(((Image)Part(bar, "PART_Icon")).Source);
        }

        Assert.Multiple(() =>
        {
            Assert.That(icons, Has.None.Null, "every severity has an icon");
            Assert.That(icons.Distinct().Count(), Is.EqualTo(4), "each its own icon");
            Assert.That(grounds.Distinct().Count(), Is.EqualTo(4), "each its own ground");
        });
    }

    [TestCaseSource(nameof(Themes))]
    public void WhatItHasNothingFor_TakesNoRoom(System.Func<Theme> theme)
    {
        Use(theme);
        var bare = Laid(new InfoBar { Title = "Saved", Message = "", IsClosable = false });
        var untitled = Laid(new InfoBar { Message = "The file is damaged." });
        var full = Laid(new InfoBar
        {
            Title = "Physics cannot be read", Message = "The file is damaged.", Content = new Button { Content = "Open" },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Part(bare, "PART_Message").Visibility, Is.EqualTo(Visibility.Collapsed), "an empty message");
            Assert.That(Part(untitled, "PART_Title").Visibility, Is.EqualTo(Visibility.Collapsed), "no title");
            Assert.That(Part(bare, "PART_Actions").Visibility, Is.EqualTo(Visibility.Collapsed), "no actions");
            Assert.That(Part(bare, "PART_CloseButton").Visibility, Is.EqualTo(Visibility.Collapsed), "not closable");
            Assert.That(Part(full, "PART_Message").Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Part(full, "PART_Actions").Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Part(full, "PART_CloseButton").Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(bare.DesiredSize.Height, Is.LessThan(full.DesiredSize.Height));
        });
    }

    [TestCaseSource(nameof(Themes))]
    public void ItsCloseButton_ClosesIt_AndClosedItTakesNoRoom(System.Func<Theme> theme)
    {
        Use(theme);
        var bar = Laid(new InfoBar { Title = "Physics cannot be read", Message = "The file is damaged." });
        var closed = 0;
        bar.Closed += (_, _) => closed++;
        var open = bar.DesiredSize.Height;

        ((ButtonBase)Part(bar, "PART_CloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Laid(bar);

        Assert.Multiple(() =>
        {
            Assert.That(open, Is.GreaterThan(0));
            Assert.That(bar.IsOpen, Is.False);
            Assert.That(closed, Is.EqualTo(1), "it says it closed");
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(bar.DesiredSize.Height, Is.Zero, "closed, it takes no room");
        });
    }

    // Shut by whoever shows it, it says nothing - they know; closed, it takes no room whatever Visibility it was given.
    [TestCaseSource(nameof(Themes))]
    public void ShutByWhoeverShowsIt_ItSaysNothing_AndTakesNoRoomWhateverItsVisibility(System.Func<Theme> theme)
    {
        Use(theme);
        var bar = Laid(new InfoBar { Title = "Saved", Visibility = Visibility.Visible });
        var closed = 0;
        bar.Closed += (_, _) => closed++;

        bar.IsOpen = false;
        Laid(bar);
        var shut = (bar.Visibility, bar.DesiredSize.Height);
        bar.IsOpen = true;
        Laid(bar);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.Zero, "set by its owner, it is not 'closed by the user'");
            Assert.That(shut.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(shut.Height, Is.Zero);
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible), "open again, it shows again");
        });
    }

    [Test]
    public void ToAutomation_ItIsAGroupNamedByItsTitle()
    {
        var peer = new InfoBarAutomationPeer(new InfoBar { Title = "Physics cannot be read" });

        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(Adamantium.UI.Core.Automation.AutomationControlType.Group));
            Assert.That(peer.Name, Is.EqualTo("Physics cannot be read"));
        });
    }
}
