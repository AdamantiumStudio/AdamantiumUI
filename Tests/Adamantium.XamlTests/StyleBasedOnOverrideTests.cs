using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// A style BasedOn another overrides the base's setter of the same property for good: the base's live resource, coming in
// or moving after the style is attached, must not write over it.
[TestFixture]
public class StyleBasedOnOverrideTests
{
    private ResourceManager _resources;
    private FakeApp _app;
    private Border _owner;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer());
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void FreshResources()
    {
        // The context is process-global and another fixture may have set it first: this one's app, with a resource
        // manager of its own, for each test.
        _resources = new ResourceManager();
        _app.ResourceManager = _resources;
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        _owner = new Border();
        _resources.AddSource(_owner, typeof(FirstResources), ResourceScope.Global);
    }

    [Test]
    public void TheDerivedValueStaysWhenTheBasesResourceMoves()
    {
        var (control, _) = Styled();
        Assert.That(control.Content, Is.EqualTo("DERIVED"), "the derived style's own value, at once");

        _resources.RemoveSources(_owner);
        _resources.AddSource(_owner, typeof(SecondResources), ResourceScope.Global);
        _resources.FlushResourceChanges();

        Assert.That(control.Content, Is.EqualTo("DERIVED"),
            "the base's resource moving - a theme swap - must not take back what the derived style said");
    }

    [Test]
    public void ABaseSetterOfAnotherPropertyStillApplies()
    {
        var (control, _) = Styled();

        Assert.That(control.Tag, Is.EqualTo("FIRST"), "only what the derived style sets is taken over");

        _resources.RemoveSources(_owner);
        _resources.AddSource(_owner, typeof(SecondResources), ResourceScope.Global);
        _resources.FlushResourceChanges();

        Assert.That(control.Tag, Is.EqualTo("SECOND"), "and the base's own resource still follows its moves");
    }

    [Test]
    public void DetachedTheStyleLeavesNoResourceBehind()
    {
        var (control, derived) = Styled();

        derived.Detach(control);
        _resources.RemoveSources(_owner);
        _resources.AddSource(_owner, typeof(SecondResources), ResourceScope.Global);
        _resources.FlushResourceChanges();

        Assert.That(control.Content, Is.Not.EqualTo("SECOND"), "a base setter passed over never connected");
        Assert.That(control.Tag, Is.Not.EqualTo("SECOND"), "the base's own resource went with the style");
    }

    [Test]
    public void ClassesGivenByTheBaseAndTheDerivedStyleAddUp()
    {
        var theme = new Theme("classes");
        var set = new StyleSet();
        var baseStyle = new Style { Selector = new StyleSelector { Types = { typeof(ToggleButton) } } };
        baseStyle.Setters.Add(new Setter("Classes", "FromBase"));
        var derived = new Style
        {
            Selector = new StyleSelector { Types = { typeof(ToggleSwitch) } },
            BasedOn = new StyleSelector { Types = { typeof(ToggleButton) } }
        };
        derived.Setters.Add(new Setter("Classes", "FromDerived"));
        set.Add(baseStyle);
        set.Add(derived);
        theme.AddStyleSet(set);

        var control = new ToggleSwitch();
        derived.Attach(control);

        Assert.Multiple(() =>
        {
            Assert.That(control.ClassNames, Does.Contain("FromBase"), "classes are given, not set over");
            Assert.That(control.ClassNames, Does.Contain("FromDerived"));
        });
    }

    // A base style for ToggleButton giving Content and Tag a live resource, and a ToggleSwitch style based on it that
    // sets Content itself.
    private static (ToggleSwitch Control, Style Derived) Styled()
    {
        var theme = new Theme("override");
        var set = new StyleSet();

        var baseStyle = new Style { Selector = new StyleSelector { Types = { typeof(ToggleButton) } } };
        baseStyle.Setters.Add(new Setter("Content", new ObservableResource("Word")));
        baseStyle.Setters.Add(new Setter("Tag", new ObservableResource("Word")));

        var derived = new Style
        {
            Selector = new StyleSelector { Types = { typeof(ToggleSwitch) } },
            BasedOn = new StyleSelector { Types = { typeof(ToggleButton) } }
        };
        derived.Setters.Add(new Setter("Content", "DERIVED"));

        set.Add(baseStyle);
        set.Add(derived);
        theme.AddStyleSet(set);

        var control = new ToggleSwitch();
        derived.Attach(control);
        return (control, derived);
    }

    private sealed class FirstResources : ResourceDictionary
    {
        protected override void OnInitialize() => Add("Word", "FIRST");
    }

    private sealed class SecondResources : ResourceDictionary
    {
        protected override void OnInitialize() => Add("Word", "SECOND");
    }
}
