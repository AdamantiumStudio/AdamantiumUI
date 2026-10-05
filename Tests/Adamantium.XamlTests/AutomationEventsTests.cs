using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// What changes on an element is told to whoever listens to automation - the accessibility bridge of the platform - with
/// the element's peer and, for a property, the old and new value in automation's own terms.
/// </summary>
[TestFixture]
public class AutomationEventsTests
{
    private FakeApp _app;
    private readonly List<AutomationEventArgs> _heard = new();

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void Fresh()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
        _heard.Clear();
    }

    [TearDown]
    public void StopListening() => AutomationEvents.Raised -= Hear;

    private void Hear(object sender, AutomationEventArgs e) => _heard.Add(e);

    private void Listen(params UIComponent[] seen)
    {
        foreach (var element in seen)
        {
            element.GetAutomationPeer();
        }

        AutomationEvents.Raised += Hear;
    }

    private static Window Shown(UIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        for (var i = 0; i < 3; i++)
        {
            window.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            BindingUpdateQueue.Flush();
            window.LayoutPopups();
        }

        return window;
    }

    private static StackPanel Holding(params IMeasurableComponent[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }

        return stack;
    }

    [Test]
    public void AClick_IsTold_AsInvokedOnTheButton()
    {
        var save = new Button { Content = "Save" };
        Shown(Holding(save));
        Listen();

        save.PerformClick();

        var invoked = _heard.Single(e => e.Event == AutomationEvent.Invoked);
        Assert.That(invoked.Element, Is.SameAs(save));
        Assert.That(invoked.Peer, Is.SameAs(save.GetAutomationPeer()));
    }

    [Test]
    public void ACheck_IsTold_AsTheToggleStateGoingFromOffToOn()
    {
        var wrap = new CheckBox { Content = "Wrap" };
        Shown(Holding(wrap));
        Listen(wrap);

        wrap.IsChecked = true;

        var change = _heard.Single(e => e.Property == AutomationProperty.ToggleState);
        Assert.That(change.Element, Is.SameAs(wrap));
        Assert.That(change.OldValue, Is.EqualTo(ToggleState.Off));
        Assert.That(change.NewValue, Is.EqualTo(ToggleState.On));
    }

    [Test]
    public void AClickOnACheckBox_IsTold_AsToggled_NotAsInvoked()
    {
        var wrap = new CheckBox { Content = "Wrap" };
        Shown(Holding(wrap));
        Listen(wrap);

        wrap.PerformClick();

        Assert.That(_heard.Select(e => e.Event), Is.EqualTo(new[] { AutomationEvent.PropertyChanged }));
    }

    [Test]
    public void Expanding_IsTold_AsTheExpandCollapseStateChange()
    {
        var details = new Expander { Header = "Details", Content = "More" };
        Shown(Holding(details));
        Listen(details);

        details.IsExpanded = true;

        var change = _heard.Single(e => e.Property == AutomationProperty.ExpandCollapseState);
        Assert.That(change.OldValue, Is.EqualTo(ExpandCollapseState.Collapsed));
        Assert.That(change.NewValue, Is.EqualTo(ExpandCollapseState.Expanded));
    }

    [Test]
    public void AMovedValue_IsTold_WithTheOldAndTheNewNumber()
    {
        var volume = new Slider { Minimum = 0, Maximum = 10, Value = 2 };
        Shown(Holding(volume));
        Listen(volume);

        volume.Value = 7;

        var change = _heard.Single(e => e.Property == AutomationProperty.RangeValue);
        Assert.That(change.OldValue, Is.EqualTo(2.0));
        Assert.That(change.NewValue, Is.EqualTo(7.0));
    }

    [Test]
    public void TheFocus_IsTold_ForTheElementThatGotIt()
    {
        var save = new Button { Content = "Save" };
        var open = new Button { Content = "Open" };
        Shown(Holding(save, open));
        Listen();

        FocusManager.Focus(open);

        var focus = _heard.Single(e => e.Event == AutomationEvent.FocusChanged);
        Assert.That(focus.Element, Is.SameAs(open));
    }

    [Test]
    public void SelectingATab_IsTold_AsSelectedForItAndUnselectedForTheOneBefore()
    {
        var first = new TabItem { Header = "First", Content = "1" };
        var second = new TabItem { Header = "Second", Content = "2" };
        var tabs = new TabControl();
        tabs.Items.Add(first);
        tabs.Items.Add(second);
        Shown(tabs);
        Listen(first, second);

        tabs.SelectedIndex = 1;

        Assert.That(_heard.Single(e => e.Event == AutomationEvent.ElementSelected).Element, Is.SameAs(second));
        var selection = _heard.Where(e => e.Property == AutomationProperty.IsSelected).ToList();
        Assert.That(selection.Single(e => ReferenceEquals(e.Element, first)).NewValue, Is.EqualTo(false));
        Assert.That(selection.Single(e => ReferenceEquals(e.Element, second)).NewValue, Is.EqualTo(true));
    }

    [Test]
    public void AnElementAutomationHasNotSeen_IsNotReportedAsChanging_NorGivenAPeerForIt()
    {
        var wrap = new CheckBox { Content = "Wrap" };
        var first = new TabItem { Header = "First", Content = "1" };
        var second = new TabItem { Header = "Second", Content = "2" };
        var tabs = new TabControl();
        tabs.Items.Add(first);
        tabs.Items.Add(second);
        Shown(Holding(wrap, tabs));
        Listen();

        wrap.IsChecked = true;
        tabs.SelectedIndex = 1;

        Assert.That(_heard, Is.Empty);
        Assert.That(wrap.FindAutomationPeer(), Is.Null);
        Assert.That(second.FindAutomationPeer(), Is.Null);
    }
}
