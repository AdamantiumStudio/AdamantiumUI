using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.MarkupExtensions;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A style without a key among an element's resources reaches the elements of its subtree its selector
/// matches: after the theme, before the element's own styles, every scope from the outermost to the nearest - and not
/// into a control's template.</summary>
[TestFixture]
public class ScopedStyleTests
{
    private static readonly SolidColorBrush Red = new(Colors.Red);
    private static readonly SolidColorBrush Blue = new(Colors.Blue);
    private static readonly SolidColorBrush Green = new(Colors.Green);

    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void UseFluent()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var fluent = new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(fluent.Name, fluent);
        themes.SetTheme(fluent);
    }

    private static Style StyleOf(string selector, string property, object value)
    {
        var style = new Style { Selector = TypeParser.Parse<StyleSelector>(selector) };
        style.Add(new Setter(property, value));
        return style;
    }

    private static ResourceDictionary StylesOf(params Style[] styles)
    {
        var resources = new ResourceDictionary();
        resources.Styles.AddRange(styles);
        return resources;
    }

    private static T Owning<T>(T owner, params Style[] styles) where T : AdamantiumComponent
    {
        ResourceContext.SetResources(owner, StylesOf(styles));
        return owner;
    }

    private static Border Plate() => new() { Classes = TypeParser.Parse<Classes>("Plate") };

    private static void Theme(MeasurableUIComponent root)
    {
        root.ApplyCurrentTheme();
        root.Measure(new Size<double>(400, 300));
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
    }

    [Test]
    public void AStyleOfASubtreeReachesTheElementsItMatches()
    {
        var plate = Plate();
        var other = new Border();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(plate);
        scope.Children.Add(other);

        Theme(scope);

        Assert.Multiple(() =>
        {
            Assert.That(plate.Background, Is.SameAs(Red));
            Assert.That(other.Background, Is.Not.SameAs(Red), "a Border without the class is not matched");
        });
    }

    [Test]
    public void ItStaysInsideTheSubtree()
    {
        var inside = Plate();
        var outside = Plate();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(inside);
        var page = new StackPanel();
        page.Children.Add(scope);
        page.Children.Add(outside);

        Theme(page);

        Assert.Multiple(() =>
        {
            Assert.That(inside.Background, Is.SameAs(Red));
            Assert.That(outside.Background, Is.Not.SameAs(Red), "a sibling of the scope is outside it");
        });
    }

    [Test]
    public void EveryScopeAppliesTheNearestLast()
    {
        var plate = Plate();
        var inner = Owning(new Grid(), StyleOf("Border.Plate", "Background", Blue));
        inner.Children.Add(plate);
        var outer = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red),
            StyleOf("Border.Plate", "BorderThickness", "3"));
        outer.Children.Add(inner);

        Theme(outer);

        Assert.Multiple(() =>
        {
            Assert.That(plate.Background, Is.SameAs(Blue), "the nearest scope's value wins");
            Assert.That(plate.BorderThickness, Is.EqualTo(new Thickness(3)), "what only the outer scope says still applies");
        });
    }

    [Test]
    public void AStyleBothScopesGiveCountsAsTheNearestOnes()
    {
        var red = StyleOf("Border.Plate", "Background", Red);
        var plate = Plate();
        var inner = Owning(new Grid(), red);
        inner.Children.Add(plate);
        var outer = Owning(new Grid(), red, StyleOf("Border.Plate", "Background", Blue));
        outer.Children.Add(inner);

        Theme(outer);

        Assert.That(plate.Background, Is.SameAs(Red), "the inner scope gives it again, after the outer scope's blue");
    }

    [Test]
    public void ResourcesGivenAfterTheChildrenStillReachThem()
    {
        var plate = Plate();
        var scope = new Grid();
        scope.Children.Add(plate);
        Theme(scope);

        Owning(scope, StyleOf("Border.Plate", "Background", Red));

        Assert.That(plate.Background, Is.SameAs(Red));
    }

    [Test]
    public void ReplacedResourcesTakeTheirStylesAlong()
    {
        var plate = Plate();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(plate);
        Theme(scope);

        ResourceContext.SetResources(scope, StylesOf(StyleOf("Border.Plate", "BorderThickness", "4")));

        Assert.Multiple(() =>
        {
            Assert.That(plate.Background, Is.Not.SameAs(Red), "the first resources' style is gone");
            Assert.That(plate.BorderThickness, Is.EqualTo(new Thickness(4)));
        });
    }

    [Test]
    public void ClearedResourcesTakeTheirStylesAlong()
    {
        var plate = Plate();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(plate);
        Theme(scope);

        ResourceContext.SetResources(scope, null);

        Assert.That(plate.Background, Is.Not.SameAs(Red));
    }

    [Test]
    public void AnElementLeavingTheScopeLosesItsStyles()
    {
        var plate = Plate();
        var holder = new Grid();
        holder.Children.Add(plate);
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(holder);
        var outside = new StackPanel();
        var page = new StackPanel();
        page.Children.Add(scope);
        page.Children.Add(outside);
        Theme(page);

        scope.Children.Remove(holder);
        outside.Children.Add(holder);
        Theme(page);

        Assert.That(plate.Background, Is.Not.SameAs(Red), "a descendant of the moved element leaves the scope with it");
    }

    [Test]
    public void AScopedValueOutlivesATheme()
    {
        var plate = Plate();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(plate);
        Theme(scope);

        var graphite = new Adamantium.UI.Themes.GraphiteTheme.Graphite();
        _app.ThemeManager.AddTheme(graphite.Name, graphite);
        _app.ThemeManager.SetTheme(graphite);
        Theme(scope);

        Assert.That(plate.Background, Is.SameAs(Red));
    }

    [Test]
    public void TheElementsOwnStyleWinsOverItsScopes()
    {
        var plate = Plate();
        plate.Styles.Add(StyleOf("Border", "Background", Green));
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(plate);

        Theme(scope);

        Assert.That(plate.Background, Is.SameAs(Green));
    }

    [Test]
    public void AScopeWinsOverTheTheme()
    {
        var button = new Button { Classes = TypeParser.Parse<Classes>("Plate") };
        var scope = Owning(new Grid(), StyleOf("Button.Plate", "Foreground", Red));
        scope.Children.Add(button);

        Theme(scope);

        Assert.That(button.Foreground, Is.SameAs(Red), "Fluent gives a button its foreground; the scope says otherwise");
    }

    [Test]
    public void AScopedStyleSurvivesAClassChange()
    {
        var plate = Plate();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(plate);
        Theme(scope);

        plate.Classes = TypeParser.Parse<Classes>("Plate Wide");

        Assert.That(plate.Background, Is.SameAs(Red));
    }

    [Test]
    public void AScopeDoesNotReachIntoAControlsTemplate()
    {
        var button = new Button { Content = "x" };
        var scope = Owning(new Grid(), StyleOf("Border", "BorderThickness", "7"));
        scope.Children.Add(button);

        Theme(scope);

        var parts = button.GetVisualDescendants().OfType<Border>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(parts, Is.Not.Empty, "the button's template has borders to test against");
            Assert.That(parts.Any(part => part.BorderThickness == new Thickness(7)), Is.False,
                "the button's template is the theme's");
        });
    }

    [Test]
    public void AScopeReachesTheContentOfAControl()
    {
        var plate = Plate();
        var host = new ContentControl { Content = plate };
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(host);

        Theme(scope);

        Assert.That(plate.Background, Is.SameAs(Red), "content belongs to the page, though a template presents it");
    }

    [Test]
    public void AScopeReachesWhatADataTemplateMakes()
    {
        var host = new ContentControl
        {
            Content = "data",
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = Plate() }),
        };
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(host);

        Theme(scope);
        Theme(scope);

        var made = host.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("Plate")).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(made, Has.Count.EqualTo(1), "the data template made its plate");
            Assert.That(made.Select(b => b.Background), Is.All.SameAs(Red));
        });
    }

    [Test]
    public void AScopeReachesTheItemsOfAList()
    {
        var list = new ListBox
        {
            ItemsSource = new[] { "a", "b" },
            ItemTemplate = new DataTemplate(() => new TemplateResult { RootComponent = Plate() }),
        };
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(list);

        Theme(scope);
        Theme(scope);

        var plates = list.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("Plate")).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(plates, Has.Count.EqualTo(2), "each item made its plate");
            Assert.That(plates.Select(b => b.Background), Is.All.SameAs(Red));
        });
    }

    private static ContentControl ShowingThrough(ContentControl part)
    {
        return new ContentControl
        {
            Template = new ControlTemplate(() =>
            {
                var result = new TemplateResult { RootComponent = part };
                result.AddTemplateBinding(part, "Content", new TemplateBinding { Path = "Content" });
                return result;
            }),
        };
    }

    [Test]
    public void AScopeReachesContentATemplatePartIsHandedOver()
    {
        var plate = Plate();
        var outer = ShowingThrough(new ContentControl());
        outer.Content = plate;
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(outer);

        Theme(scope);
        Theme(scope);

        Assert.That(plate.Background, Is.SameAs(Red), "the page's content, though a part of the template shows it");
    }

    [Test]
    public void AScopeDoesNotReachWhatATemplateWritesIntoAPart()
    {
        var inner = new ContentControl();
        var outer = new ContentControl
        {
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = inner }),
        };
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(outer);
        Theme(scope);

        var plate = Plate();
        inner.Content = plate;
        Theme(scope);

        Assert.That(plate.Background, Is.Not.SameAs(Red), "what the template or its control's code puts in is the theme's");
    }

    [Test]
    public void AScopeDoesNotReachTextATemplateWritesIntoAPart()
    {
        var label = new ContentControl { Content = "x" };
        var outer = new ContentControl
        {
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = label }),
        };
        var scope = Owning(new Grid(), StyleOf("TextBlock", "Margin", "13"));
        scope.Children.Add(outer);

        Theme(scope);
        Theme(scope);

        var texts = label.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(texts, Is.Not.Empty, "the part shows its text");
            Assert.That(texts.Select(t => t.Margin), Has.None.EqualTo(new Thickness(13)));
        });
    }

    [Test]
    public void ATemplateGoneLeavesThePartsOfItsContentAlone()
    {
        var button = new Button { Content = "x" };
        var host = new ContentControl { Content = button };
        var scope = new Grid();
        scope.Children.Add(host);
        Theme(scope);
        var parts = button.GetVisualDescendants().OfType<FundamentalUIComponent>()
            .Where(p => p.IsControlTemplatePart && p.TemplatedParent == button).ToList();

        host.Template = new ControlTemplate(() =>
        {
            var presenter = new ContentPresenter();
            var result = new TemplateResult { RootComponent = presenter };
            result.AddTemplateBinding(presenter, "Content", new TemplateBinding { Path = "Content" });
            return result;
        });
        Theme(scope);

        Assert.Multiple(() =>
        {
            Assert.That(parts, Is.Not.Empty, "the button has a template");
            Assert.That(parts.All(p => p.IsControlTemplatePart && p.TemplatedParent == button), Is.True,
                "its host's template going does not take the button's own parts with it");
        });
    }

    [Test]
    public void AHeaderATemplateWritesStaysTheThemesWhileTheContentItHandsOnIsThePages()
    {
        var page = new TextBlock { Text = "page" };
        var outer = new ContentControl { Content = page };
        outer.Template = new ControlTemplate(() =>
        {
            var expander = new Expander { Header = "literal", IsExpanded = true };
            var result = new TemplateResult { RootComponent = expander };
            result.AddTemplateBinding(expander, "Content", new TemplateBinding { Path = "Content" });
            return result;
        });
        var scope = Owning(new Grid(), StyleOf("TextBlock", "Margin", "13"));
        scope.Children.Add(outer);

        Theme(scope);
        Theme(scope);

        var literal = outer.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "literal");
        Assert.Multiple(() =>
        {
            Assert.That(page.Margin, Is.EqualTo(new Thickness(13)));
            Assert.That(literal.Margin, Is.Not.EqualTo(new Thickness(13)), "the header the template wrote");
        });
    }

    [Test]
    public void AHeaderHandedOnThroughATemplateIsThePages()
    {
        var page = new TextBlock { Text = "page" };
        var outer = new Expander { Header = page };
        outer.Template = new ControlTemplate(() =>
        {
            var expander = new Expander { Content = "literal", IsExpanded = true };
            var result = new TemplateResult { RootComponent = expander };
            result.AddTemplateBinding(expander, "Header", new TemplateBinding { Path = "Header" });
            return result;
        });
        var scope = Owning(new Grid(), StyleOf("TextBlock", "Margin", "13"));
        scope.Children.Add(outer);

        Theme(scope);
        Theme(scope);

        Assert.That(page.Margin, Is.EqualTo(new Thickness(13)));
    }

    [Test]
    public void APartGivenAnotherTemplateKeepsTheOuterTemplatesParts()
    {
        var page = new TextBlock { Text = "page" };
        Button part = null;
        StackPanel written = null;
        var outer = new ContentControl { Content = page };
        outer.Template = new ControlTemplate(() =>
        {
            var presenter = new ContentPresenter();
            written = new StackPanel();
            written.Children.Add(presenter);
            part = new Button { Content = written };
            var result = new TemplateResult { RootComponent = part };
            result.AddTemplateBinding(presenter, "Content", new TemplateBinding { Path = "Content" });
            return result;
        });
        var scope = Owning(new Grid(), StyleOf("TextBlock", "Margin", "13"));
        scope.Children.Add(outer);
        Theme(scope);

        part.Template = new ControlTemplate(() =>
        {
            var presenter = new ContentPresenter();
            var result = new TemplateResult { RootComponent = new Border { Child = presenter } };
            result.AddTemplateBinding(presenter, "Content", new TemplateBinding { Path = "Content" });
            return result;
        });
        Theme(scope);
        Owning(scope, StyleOf("TextBlock", "Margin", "13"));

        Assert.Multiple(() =>
        {
            Assert.That(written.IsControlTemplatePart, Is.True, "written into the outer template, still its part");
            Assert.That(page.Margin, Is.EqualTo(new Thickness(13)));
        });
    }

    [Test]
    public void AScopeReachesTheHeaderOfAColumn()
    {
        var header = new TextBlock { Text = "header" };
        var grid = new TreeDataGrid { ItemsSource = new[] { "a" } };
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding("Length") });
        var scope = Owning(new Grid(), StyleOf("TextBlock", "Margin", "13"));
        scope.Children.Add(grid);

        Theme(scope);
        Theme(scope);

        Assert.That(header.Margin, Is.EqualTo(new Thickness(13)), "the page gave the column its header");
    }

    [Test]
    public void APopupOutOfTheTreeStandsInTheScopeOfItsTarget()
    {
        var tip = new TextBlock { Text = "tip" };
        var target = new Button { Content = "b" };
        var scope = Owning(new Grid(), StyleOf("TextBlock", "Margin", "13"));
        scope.Children.Add(target);
        Theme(scope);

        var popup = new Popup { Child = tip };
        popup.PlacementTarget = target;
        Theme(popup);

        Assert.That(tip.Margin, Is.EqualTo(new Thickness(13)));
    }

    [Test]
    public void ADictionaryLinkedTwiceKeepsWhatItIncludesTillBothLinksGo()
    {
        var plate = Plate();
        var scope = new Grid();
        ResourceContext.SetSource(scope, new ResourceLink { Source = typeof(ScopedPlateDictionary) });
        var resources = new ResourceDictionary();
        resources.Includes.Add(new ResourceLink { Source = typeof(ScopedPlateDictionary) });
        ResourceContext.SetResources(scope, resources);
        scope.Children.Add(plate);
        Theme(scope);

        ResourceContext.SetResources(scope, null);
        var kept = plate.BorderThickness;
        ResourceContext.SetSource(scope, null);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(new Thickness(5)), "the Source link still includes the styles");
            Assert.That(plate.BorderThickness, Is.Not.EqualTo(new Thickness(5)), "both links gone");
        });
    }

    [Test]
    public void ADictionaryWithStylesInItsIncludesComesAndGoesLater()
    {
        var plate = Plate();
        var scope = new Grid();
        scope.Children.Add(plate);
        Theme(scope);

        ResourceContext.SetSource(scope, new ResourceLink { Source = typeof(ScopedPlateDictionary) });
        var given = plate.BorderThickness;
        ResourceContext.SetSource(scope, null);

        Assert.Multiple(() =>
        {
            Assert.That(given, Is.EqualTo(new Thickness(5)));
            Assert.That(plate.BorderThickness, Is.Not.EqualTo(new Thickness(5)));
        });
    }

    [Test]
    public void AnIncludeGoesWithItsResourcesThoughTheScopeChangedMeanwhile()
    {
        var plate = Plate();
        var scope = new Grid();
        var resources = new ResourceDictionary();
        resources.Includes.Add(new ResourceLink { Source = typeof(ScopedPlateStyles) });
        ResourceContext.SetResources(scope, resources);
        scope.Children.Add(plate);
        Theme(scope);

        ResourceContext.SetScope(scope, ResourceScope.Global);
        ResourceContext.SetResources(scope, null);

        Assert.That(plate.BorderThickness, Is.Not.EqualTo(new Thickness(5)));
    }

    [Test]
    public void AStyleSetLinkedTwiceStaysTillBothLinksGo()
    {
        var plate = Plate();
        var scope = new Grid();
        ResourceContext.SetSource(scope, new ResourceLink { Source = typeof(ScopedPlateStyles) });
        var resources = new ResourceDictionary();
        resources.Includes.Add(new ResourceLink { Source = typeof(ScopedPlateStyles) });
        ResourceContext.SetResources(scope, resources);
        scope.Children.Add(plate);
        Theme(scope);

        ResourceContext.SetResources(scope, null);
        Theme(scope);

        Assert.That(plate.BorderThickness, Is.EqualTo(new Thickness(5)), "the Source link still has it");
    }

    [Test]
    public void AStyleAddedToResourcesAlreadyGivenDoesNotUpsetOtherScopes()
    {
        var plate = Plate();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(plate);
        var other = new Grid();
        var late = new ResourceDictionary();
        ResourceContext.SetResources(other, late);
        var page = new StackPanel();
        page.Children.Add(scope);
        page.Children.Add(other);
        Theme(page);

        late.Styles.Add(StyleOf("Border", "Background", Blue));
        ResourceContext.SetResources(other, null);
        plate.ApplyCurrentTheme();

        Assert.That(plate.Background, Is.SameAs(Red));
    }

    [Test]
    public void AScopeDoesNotReachWhatAPartsCodeMakes()
    {
        var field = new DateTimeField();
        var scope = Owning(new Grid(), StyleOf("TextBlock", "FontSize", "77"));
        scope.Children.Add(field);

        Theme(scope);
        Theme(scope);

        var texts = field.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(texts, Is.Not.Empty, "the field shows text to test against");
            Assert.That(texts.Select(t => t.FontSize), Has.None.EqualTo(77d), "the field's look is the theme's");
        });
    }

    [Test]
    public void ResourcesGivenLaterReachTheContentOfAControl()
    {
        var plate = Plate();
        var scope = new Grid();
        scope.Children.Add(new ContentControl { Content = plate });
        Theme(scope);

        Owning(scope, StyleOf("Border.Plate", "Background", Red));

        Assert.That(plate.Background, Is.SameAs(Red));
    }

    [Test]
    public void ClearedResourcesLeaveTheContentOfAControl()
    {
        var plate = Plate();
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(new ContentControl { Content = plate });
        Theme(scope);

        ResourceContext.SetResources(scope, null);

        Assert.That(plate.Background, Is.Not.SameAs(Red));
    }

    [Test]
    public void TheContentOfAControlLeavesTheScopeWithIt()
    {
        var plate = Plate();
        var host = new ContentControl { Content = plate };
        var scope = Owning(new Grid(), StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(host);
        var outside = new StackPanel();
        var page = new StackPanel();
        page.Children.Add(scope);
        page.Children.Add(outside);
        Theme(page);

        scope.Children.Remove(host);
        outside.Children.Add(host);

        Assert.That(plate.Background, Is.Not.SameAs(Red));
    }

    [Test]
    public void AScopedStyleByTypeYieldsToAnInheritedValueAndOneWithAClassDoesNot()
    {
        var bare = new TextBlock { Text = "bare" };
        var classed = new TextBlock { Text = "classed", Classes = TypeParser.Parse<Classes>("Caption") };
        var panel = new StackPanel();
        panel.Children.Add(bare);
        panel.Children.Add(classed);
        var scope = Owning(new Grid(), StyleOf("TextBlock", "Foreground", Red),
            StyleOf("TextBlock.Caption", "Foreground", Green));
        scope.Children.Add(new ContentControl { Foreground = Blue, Content = panel });

        Theme(scope);

        Assert.Multiple(() =>
        {
            Assert.That(bare.Foreground, Is.SameAs(Blue), "a rule by type alone does not mask what an ancestor says");
            Assert.That(classed.Foreground, Is.SameAs(Green), "one naming a class means those elements");
        });
    }

    [Test]
    public void AStyleSetLinkedInTheResourcesReachesTheSubtree()
    {
        var plate = Plate();
        var scope = new Grid();
        ResourceContext.SetSource(scope, new ResourceLink { Source = typeof(ScopedPlateStyles) });
        scope.Children.Add(plate);

        Theme(scope);

        Assert.Multiple(() =>
        {
            Assert.That(plate.Background, Is.InstanceOf<SolidColorBrush>().And.Property("Color").EqualTo(Colors.Red));
            Assert.That(plate.BorderThickness, Is.EqualTo(new Thickness(5)), "what the set makes when initialized");
        });
    }

    [Test]
    public void ALinkTakenAwayTakesItsStylesAlong()
    {
        var plate = Plate();
        var scope = new Grid();
        var link = new ResourceLink { Source = typeof(ScopedPlateStyles) };
        ResourceContext.SetSource(scope, link);
        scope.Children.Add(plate);
        Theme(scope);

        ResourceContext.SetSource(scope, new ResourceLink { Source = typeof(ScopedPlateStyles) });
        ResourceContext.SetSource(scope, null);

        Assert.That(plate.BorderThickness, Is.Not.EqualTo(new Thickness(5)));
    }

    [Test]
    public void AStyleSetIncludedInTheResourcesComesAndGoesWithThem()
    {
        var plate = Plate();
        var scope = new Grid();
        scope.Children.Add(plate);
        Theme(scope);

        var resources = new ResourceDictionary();
        resources.Includes.Add(new ResourceLink { Source = typeof(ScopedPlateStyles) });
        ResourceContext.SetResources(scope, resources);
        var given = plate.BorderThickness;
        ResourceContext.SetResources(scope, null);
        Theme(scope);

        Assert.Multiple(() =>
        {
            Assert.That(given, Is.EqualTo(new Thickness(5)));
            Assert.That(plate.BorderThickness, Is.Not.EqualTo(new Thickness(5)), "gone with the resources");
        });
    }

    [Test]
    public void GlobalResourcesStillGiveTheirStylesOnlyToTheirSubtree()
    {
        var inside = Plate();
        var outside = Plate();
        var scope = new Grid();
        ResourceContext.SetScope(scope, ResourceScope.Global);
        Owning(scope, StyleOf("Border.Plate", "Background", Red));
        scope.Children.Add(inside);
        var page = new StackPanel();
        page.Children.Add(scope);
        page.Children.Add(outside);

        Theme(page);

        Assert.Multiple(() =>
        {
            Assert.That(inside.Background, Is.SameAs(Red));
            Assert.That(outside.Background, Is.Not.SameAs(Red));
        });
    }

    [Test]
    public void AStyleWithoutAKeyBuildsFromMarkup()
    {
        const string body = """
            <Grid><ResourceContext.Resources><Style Selector="Border.Plate"><Setter Property="BorderThickness" Value="2"/></Style></ResourceContext.Resources><Border Classes="Plate"/></Grid>
            """;

        var errors = AumlCodegenHarness.Diagnose("MainWindow.auml", AumlCodegenHarness.WindowHeader + ">" + body + "</Window>",
                compile: true)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();
        var loaded = AumlLoader.Load(body.Replace("<Grid>",
            "<Grid xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">"));

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "the build takes it");
            Assert.That(loaded.Diagnostics, Is.Empty, "the preview takes it");
        });

        var grid = (Grid)loaded.Root;
        Theme(grid);
        Assert.That(((Border)grid.Children[0]).BorderThickness, Is.EqualTo(new Thickness(2)));
    }
}
