using System;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.MarkupExtensions;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A STYLE WITHOUT A KEY AMONG AN ELEMENT'S RESOURCES styles the elements of its logical subtree its selector
/// matches. The nearest scope wins whole - specificity decides only within one scope; the theme's styles stand below
/// every scope, an element's own above them all. The application's resources are the outermost scope, a theme's are the
/// theme's; from a view's resources, a style for every element is refused.</summary>
[TestFixture]
public class ScopedStyleTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void Use()
    {
        _app.ResourceManager = new ResourceManager();
        _app.Windows = [];
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Window Shown(IUIComponent content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.ApplyCurrentTheme();
        Settle(window);
        return window;
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            BindingUpdateQueue.Flush();
        }
    }

    private static Style StyleOf(Type type, string property, object value, string className = null)
    {
        var style = new Style();
        style.Selector.Types.Add(type);
        if (className != null) style.Selector.Classes.Add(className);
        style.Setters.Add(new Setter(property, value));
        return style;
    }

    private static ResourceDictionary Styles(params Style[] styles)
    {
        var resources = new ResourceDictionary();
        resources.Styles.AddRange(styles);
        return resources;
    }

    private static StackPanel Scope(params Style[] styles)
    {
        var panel = new StackPanel();
        ResourceContext.SetResources(panel, Styles(styles));
        return panel;
    }


    [Test]
    public void AStyleWithoutAKey_InAViewsResources_IsTakenAsAScopeStyle()
    {
        var result = AumlLoader.Load(
            $"<View x:Namespace=\"Test.App\" {Namespaces}><ResourceContext.Resources>" +
            "<Style Selector=\"TextBlock\"><Setter Property=\"Width\" Value=\"42\"/></Style>" +
            "<SolidColorBrush x:Key=\"Accent\" Color=\"Red\"/>" +
            "</ResourceContext.Resources></View>");

        var resources = ResourceContext.GetResources((AdamantiumComponent)result.Root);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty, "a style needs no key");
            Assert.That(resources.Styles, Has.Count.EqualTo(1));
            Assert.That(resources.ContainsKey("Accent"), "the keyed entries stay entries");
        });
    }

    [Test]
    public void AStyleWithoutAKey_InADictionaryFile_IsTakenAsAScopeStyle()
    {
        var result = AumlLoader.Load(
            $"<ResourceDictionary {Namespaces}>" +
            "<Style Selector=\"TextBlock\"><Setter Property=\"Width\" Value=\"42\"/></Style>" +
            "</ResourceDictionary>");

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty, "a style needs no key");
            Assert.That(((ResourceDictionary)result.Root).Styles, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void AScopeStyle_StylesTheElementsOfTheSubtree_AndNothingOutside()
    {
        var inside = new TextBlock { Text = "in" };
        var deeper = new TextBlock { Text = "deeper" };
        var scope = Scope(StyleOf(typeof(TextBlock), "Width", 42.0));
        scope.Children.Add(inside);
        scope.Children.Add(new Border { Child = deeper });
        var outside = new TextBlock { Text = "out" };
        Shown(new StackPanel { Children = { scope, outside } });

        Assert.Multiple(() =>
        {
            Assert.That(inside.Width, Is.EqualTo(42));
            Assert.That(deeper.Width, Is.EqualTo(42), "the whole logical subtree, not only the children");
            Assert.That(outside.Width, Is.NaN);
        });
    }

    [Test]
    public void TheScopeItself_IsNotStyledByItsOwnStyles()
    {
        var inner = new StackPanel();
        var scope = Scope(StyleOf(typeof(StackPanel), "Width", 42.0));
        scope.Children.Add(inner);
        Shown(scope);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Width, Is.NaN);
            Assert.That(inner.Width, Is.EqualTo(42));
        });
    }

    [Test]
    public void AScopeStyle_DoesNotReachIntoATemplate()
    {
        var button = new Button { Content = "Save" };
        var logical = new Border();
        var scope = Scope(StyleOf(typeof(Border), "Width", 42.0));
        scope.Children.Add(button);
        scope.Children.Add(logical);
        Shown(scope);

        var parts = button.GetVisualDescendants().OfType<Border>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(parts, Is.Not.Empty, "the theme's button is drawn by a border");
            Assert.That(parts.Select(p => p.Width), Has.None.EqualTo(42), "a template's parts are its control's, not the view's");
            Assert.That(logical.Width, Is.EqualTo(42));
        });
    }

    [Test]
    public void AControlsLabel_AndAListsRows_AreStyledByTheScopeOverThem()
    {
        var button = new Button { Content = "Save" };
        var list = new ListBox { ItemsSource = new[] { "One", "Two" } };
        var scope = Scope(StyleOf(typeof(TextBlock), "Tag", "scoped"));
        scope.Children.Add(button);
        scope.Children.Add(list);
        Shown(scope);

        var label = button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Save");
        var rows = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is "One" or "Two").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(label, Is.Not.Null);
            Assert.That(label.Tag, Is.EqualTo("scoped"), "a label made from the button's content is the button's");
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.Select(r => r.Tag), Is.All.EqualTo("scoped"), "a row made from an item is the list's");
        });
    }

    [Test]
    public void TheNearestScope_Wins_HoweverSpecificTheOuterSelectorIs()
    {
        var text = new TextBlock { Text = "x", Classes = Classes.Parse("Accent") };
        var inner = Scope(StyleOf(typeof(TextBlock), "Width", 20.0));
        inner.Children.Add(text);
        var outer = Scope(StyleOf(typeof(TextBlock), "Width", 10.0, className: "Accent"));
        outer.Children.Add(inner);
        Shown(outer);

        Assert.That(text.Width, Is.EqualTo(20));
    }

    [Test]
    public void WithinOneScope_TheMoreSpecificSelectorWins_WhicheverIsWrittenFirst()
    {
        var text = new TextBlock { Text = "x", Classes = Classes.Parse("Accent") };
        var scope = Scope(
            StyleOf(typeof(TextBlock), "Width", 20.0, className: "Accent"),
            StyleOf(typeof(TextBlock), "Width", 10.0));
        scope.Children.Add(text);
        Shown(scope);

        Assert.That(text.Width, Is.EqualTo(20));
    }

    [Test]
    public void AnElementsOwnStyle_WinsOverTheScopes()
    {
        var text = new TextBlock { Text = "x" };
        text.Styles.Add(StyleOf(typeof(TextBlock), "Width", 30.0));
        var scope = Scope(StyleOf(typeof(TextBlock), "Width", 20.0, className: "Accent"));
        text.Classes = Classes.Parse("Accent");
        scope.Children.Add(text);
        Shown(scope);

        Assert.That(text.Width, Is.EqualTo(30));
    }

    [Test]
    public void AScopeStyle_WinsOverTheThemes()
    {
        var button = new Button { Content = "Save" };
        var scope = Scope(StyleOf(typeof(Button), "Padding", new Thickness(7)));
        scope.Children.Add(button);
        Shown(scope);

        Assert.That(button.Padding, Is.EqualTo(new Thickness(7)));
    }

    [Test]
    public void ALiveResourceOfAScopeFurtherOut_StaysBelowTheNearer_WhenResourcesChange()
    {
        var gray = new Adamantium.UI.Core.Media.SolidColorBrush(Colors.Gray);
        var red = new Adamantium.UI.Core.Media.SolidColorBrush(Colors.Red);
        var keyed = new ResourceDictionary();
        keyed.Add("ScopeTestBrush", gray);
        _app.ResourceManager.AddSource(_app, keyed, ResourceScope.Global);
        var button = new Button { Content = "x", Classes = Classes.Parse("Accent") };
        var inner = Scope(StyleOf(typeof(Button), "Foreground", red, className: "Accent"));
        inner.Children.Add(button);
        var outer = Scope(StyleOf(typeof(Button), "Foreground", new ObservableResource("ScopeTestBrush"), className: "Accent"));
        outer.Children.Add(inner);
        var window = Shown(outer);

        _app.ResourceManager.NotifyResourcesChanged();
        _app.ResourceManager.FlushResourceChanges();
        Settle(window);

        Assert.That(button.Foreground, Is.SameAs(red),
            "a live resource re-stating its value is one of the element's style values, ranked with the others");
    }

    [Test]
    public void ATriggerOfTheNearestScope_WinsOverOneFurtherOut()
    {
        Style Disabled(double width, string className = null)
        {
            var style = new Style();
            style.Selector.Types.Add(typeof(Button));
            if (className != null) style.Selector.Classes.Add(className);
            var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
            trigger.Add(new Setter { Property = "Width", Value = width.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            style.Triggers.Add(trigger);
            return style;
        }

        var button = new Button { Content = "x", Classes = Classes.Parse("Accent") };
        var inner = Scope(Disabled(20));
        inner.Children.Add(button);
        var outer = Scope(Disabled(10, className: "Accent"));
        outer.Children.Add(inner);
        var window = Shown(outer);

        button.IsEnabled = false;
        Settle(window);

        Assert.That(button.Width, Is.EqualTo(20));
    }

    [Test]
    public void AClassGiven_TakesTheScopeStyleForIt_AndTakenAway_LetsItGo()
    {
        var text = new TextBlock { Text = "x" };
        var scope = Scope(StyleOf(typeof(TextBlock), "Width", 20.0, className: "Accent"));
        scope.Children.Add(text);
        Shown(scope);

        text.Classes = Classes.Parse("Accent");
        var given = text.Width;
        text.Classes = Classes.Parse("Plain");

        Assert.Multiple(() =>
        {
            Assert.That(given, Is.EqualTo(20));
            Assert.That(text.Width, Is.NaN);
        });
    }

    [Test]
    public void MovedOutOfTheScope_AnElementLetsItsStylesGo_AndMovedIntoAnother_TakesTheirs()
    {
        var text = new TextBlock { Text = "x" };
        var first = Scope(StyleOf(typeof(TextBlock), "Width", 20.0));
        var second = Scope(StyleOf(typeof(TextBlock), "Height", 30.0));
        var plain = new StackPanel();
        first.Children.Add(new Border { Child = text });
        var window = Shown(new StackPanel { Children = { first, second, plain } });
        var holder = (Border)text.LogicalParent;

        first.Children.Remove(holder);
        plain.Children.Add(holder);
        Settle(window);
        var unstyled = (text.Width, text.Height);

        plain.Children.Remove(holder);
        second.Children.Add(holder);
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(unstyled.Width, Is.NaN, "a subtree moved out lets its scope's styles go, its depths too");
            Assert.That(unstyled.Height, Is.NaN);
            Assert.That(text.Width, Is.NaN);
            Assert.That(text.Height, Is.EqualTo(30), "and moved under another scope takes its styles");
        });
    }

    [Test]
    public void ANewDictionary_RestylesTheSubtree_AndNoneLetsItsStylesGo()
    {
        var text = new TextBlock { Text = "x" };
        var scope = new StackPanel { Children = { text } };
        Shown(scope);

        ResourceContext.SetResources(scope, Styles(StyleOf(typeof(TextBlock), "Width", 20.0)));
        var first = text.Width;
        ResourceContext.SetResources(scope, Styles(StyleOf(typeof(TextBlock), "Height", 30.0)));
        var second = (text.Width, text.Height);
        ResourceContext.SetResources(scope, null);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(20));
            Assert.That(second.Width, Is.NaN, "the old dictionary's styles go with it");
            Assert.That(second.Height, Is.EqualTo(30));
            Assert.That(text.Height, Is.NaN);
        });
    }

    [Test]
    public void ALinkedDictionaryFile_IsAScopeToo()
    {
        var text = new TextBlock { Text = "x" };
        var scope = new StackPanel { Children = { text } };
        var resources = new ResourceDictionary();
        resources.Includes.Add(new ResourceLink { Source = typeof(WideTexts) });
        ResourceContext.SetResources(scope, resources);
        Shown(scope);

        Assert.That(text.Width, Is.EqualTo(42));
    }

    [Test]
    public void TheApplicationsStyles_AreTheOutermostScope()
    {
        _app.ResourceManager.AddSource(_app, Styles(
            StyleOf(typeof(TextBlock), "Width", 10.0, className: "Accent"),
            StyleOf(typeof(TextBlock), "Height", 10.0)), ResourceScope.Global);
        var anywhere = new TextBlock { Text = "x" };
        var scoped = new TextBlock { Text = "y", Classes = Classes.Parse("Accent") };
        var scope = Scope(StyleOf(typeof(TextBlock), "Width", 20.0));
        scope.Children.Add(scoped);
        Shown(new StackPanel { Children = { anywhere, scope } });

        Assert.Multiple(() =>
        {
            Assert.That(anywhere.Height, Is.EqualTo(10), "every element");
            Assert.That(scoped.Width, Is.EqualTo(20), "and any nearer scope wins over them");
        });
    }

    [Test]
    public void AThemesStyles_AmongItsResources_AreTheThemes()
    {
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        var resources = ResourceContext.GetResources(theme);
        if (resources == null)
        {
            resources = new ResourceDictionary();
            ResourceContext.SetResources(theme, resources);
        }

        resources.Styles.Add(StyleOf(typeof(TextBlock), "Tag", "themed"));
        var themes = (ThemeManager)_app.ThemeManager;
        themes.AddTheme("WithStyles", theme);
        themes.SetTheme(theme);
        var themed = new TextBlock { Text = "x" };
        var own = new TextBlock { Text = "y" };
        own.Styles.Add(StyleOf(typeof(TextBlock), "Tag", "own"));
        Shown(new StackPanel { Children = { themed, own } });

        Assert.Multiple(() =>
        {
            Assert.That(themed.Tag, Is.EqualTo("themed"));
            Assert.That(own.Tag, Is.EqualTo("own"), "the theme's, below what an element is given");
        });
    }

    [TestCase(ResourceScope.Global)]
    [TestCase(ResourceScope.Theme)]
    public void StylesForEveryElement_FromAViewsResources_AreRefused(ResourceScope scope)
    {
        var view = new StackPanel();
        ResourceContext.SetScope(view, scope);

        Assert.Throws<InvalidOperationException>(
            () => ResourceContext.SetResources(view, Styles(StyleOf(typeof(TextBlock), "Width", 20.0))),
            "opened again and again, a view would add them again each time and nothing would take them away");
    }

    private sealed class WideTexts : ResourceDictionary
    {
        public WideTexts() => Styles.Add(StyleOf(typeof(TextBlock), "Width", 42.0));
    }
}
