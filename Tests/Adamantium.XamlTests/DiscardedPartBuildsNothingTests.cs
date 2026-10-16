using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A PART A TRIGGER COLLAPSED, THROWN AWAY WITH ITS TEMPLATE, STAYS EMPTY. Letting go of the old template tears
/// its triggers down after its parts are discarded, and the part comes back to Visible on the way out - which must not
/// build it a template nobody will ever release.</summary>
[TestFixture]
public class DiscardedPartBuildsNothingTests
{
    private FakeApp _app;

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

    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    [Test]
    public void AButtonTheThemeCollapsed_IsNotBuiltOnTheWayOut()
    {
        Use(new Adamantium.UI.Themes.FluentTheme.Fluent());
        var spinner = new NumericUpDown { AreButtonsVisible = false };
        var window = new Window { Width = 400, Height = 300, Content = spinner };
        window.ApplyCurrentTheme();
        Settle(window);
        var old = (TemplatedUIComponent)spinner.GetTemplateChild("PART_Increase");
        Assert.That(old.Visibility, Is.EqualTo(Visibility.Collapsed), "the trigger collapsed it");

        Use(new Adamantium.UI.Themes.GraphiteTheme.Graphite());
        window.ApplyCurrentTheme();
        Settle(window);

        Assert.That(spinner.GetTemplateChild("PART_Increase"), Is.Not.SameAs(old), "the theme swap replaced the part");
        Assert.That(old.IsDiscarded, Is.True);
        Assert.That(old.VisualChildren, Is.Empty, "a template was built on the discarded part");
    }

    [Test]
    public void ADiscardedControl_ShownAgain_BuildsNothing()
    {
        Use(new Adamantium.UI.Themes.FluentTheme.Fluent());
        var button = new Button { Visibility = Visibility.Collapsed };
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.ApplyCurrentTheme();
        Settle(window);

        button.MarkDiscarded();
        button.Visibility = Visibility.Visible;

        Assert.That(button.VisualChildren, Is.Empty);
    }

    // ...but one TAKEN UP AGAIN before it was let go - a pane's body moving between panels - builds what it held back.
    [Test]
    public void ADiscardedControl_ShownThenTakenUpAgain_BuildsItsTemplate()
    {
        Use(new Adamantium.UI.Themes.FluentTheme.Fluent());
        var button = new Button { Visibility = Visibility.Collapsed };
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.ApplyCurrentTheme();
        Settle(window);

        window.Content = null;
        button.MarkDiscarded();
        button.Visibility = Visibility.Visible;
        window.Content = button;
        Settle(window);

        Assert.That(button.IsDiscarded, Is.False, "precondition: attaching revives it");
        Assert.That(button.VisualChildren, Is.Not.Empty);
    }
}
