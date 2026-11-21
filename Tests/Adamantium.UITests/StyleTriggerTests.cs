using Adamantium.Mathematics;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Panels = Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Controls;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Core.TypeParsers;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class StyleTriggerTests
{
    // Regression: a trigger setter value can be a {ResourceReference} marker. The activator must resolve it as a marker,
    // not blindly CastFromString it - the latter stringified the marker into the brush parser and crashed
    // (KeyNotFoundException "...ResourceReference") the moment a hover/press/disabled trigger first fired.
    [Test]
    public void PropertyTrigger_ResourceReferenceSetter_DoesNotReachBrushParser()
    {
        var host = new Border();
        var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter { Property = "Background", Value = new ResourceReference("ControlFillColorDefault") });

        trigger.Apply(new PartContext(host, theme: null));

        Assert.DoesNotThrow(() => host.IsEnabled = false,
            "ResourceReference marker is routed through the theme, never parsed as a color string");
    }

    // The unification: ONE template, the variants only differ in the control's state brushes. A trigger reads a brush
    // off the templated control via {TemplateBinding} and projects it onto a named part - live, and undone on exit.
    [Test]
    public void PropertyTrigger_TemplateBindingSetter_ProjectsHostBrushOntoPart()
    {
        var disabled = new SolidColorBrush(Colors.Red);
        var host = new Button { BackgroundDisabled = disabled };
        host.Template = NamedPartTemplate("InnerBorder");   // set == applied synchronously

        var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter
        {
            TargetName = "InnerBorder",
            Property = "Background",
            Value = new TemplateBinding { Path = "BackgroundDisabled" }
        });
        trigger.Apply(new PartContext(host, theme: null));

        var part = (Border)host.GetTemplateChild("InnerBorder");
        var rest = part.Background;
        Assert.That(rest, Is.Not.SameAs(disabled), "inactive before the condition holds");

        host.IsEnabled = false;
        Assert.That(part.Background, Is.SameAs(disabled), "{TemplateBinding} read the host's state brush onto the part");

        var swapped = new SolidColorBrush(Colors.RoyalBlue);
        host.BackgroundDisabled = swapped;
        Assert.That(part.Background, Is.SameAs(swapped), "live: a host brush change flows to the part while the trigger holds");

        host.IsEnabled = true;
        Assert.That(part.Background, Is.SameAs(rest), "reverts to the rest value once the condition no longer holds");
    }

    // Clockwork lifecycle: swapping a control's template at runtime must re-point an active part-targeting trigger onto
    // the NEW part and fully clean the OLD one - no stale trigger value left behind, no leaked subscription. Exercises
    // the real path: Style.Attach registers the activator on the control, and TemplatedUIComponent re-wires it on the
    // Template change (Style.ReevaluateActivators).
    [Test]
    public void StyleTrigger_TemplateSwap_RetargetsToNewPartAndCleansOld()
    {
        var disabled = new SolidColorBrush(Colors.Purple);
        var button = new Button { BackgroundDisabled = disabled };

        var style = new Style();
        style.Selector.Types.Add(typeof(Button));
        var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter
        {
            TargetName = "InnerBorder",
            Property = "Background",
            Value = new TemplateBinding { Path = "BackgroundDisabled" }
        });
        style.Triggers.Add(trigger);
        style.Attach(button);

        button.Template = NamedPartTemplate("InnerBorder");          // template A
        var partA = (Border)button.GetTemplateChild("InnerBorder");

        button.IsEnabled = false;
        Assert.That(partA.Background, Is.SameAs(disabled), "trigger applied to the first template's part");

        button.Template = NamedPartTemplate("InnerBorder");          // swap -> template B, condition still holds
        var partB = (Border)button.GetTemplateChild("InnerBorder");

        Assert.That(partB, Is.Not.SameAs(partA), "a genuinely new part instance");
        Assert.That(partB.Background, Is.SameAs(disabled), "re-pointed onto the new template's part after the swap");
        Assert.That(partA.Background, Is.Not.SameAs(disabled), "old part cleaned - no stale trigger value left behind");
    }

    // A property-condition selector ("Button[IsEnabled=false]", Avalonia-style) parses into the structural facet (type)
    // PLUS a runtime Condition; the condition does NOT gate attachment (see StyleSelector.Match / Style.Attach).
    [Test]
    public void SelectorParser_ExtractsPropertyConditions_KeepingTheStructuralPart()
    {
        var selector = new SelectorParser().Parse("Button[IsEnabled=false]");
        Assert.Multiple(() =>
        {
            Assert.That(selector.Types.Contains(typeof(Button)), Is.True, "the structural type is still parsed");
            Assert.That(selector.HasConditions, Is.True);
            Assert.That(selector.Conditions, Has.Count.EqualTo(1));
            Assert.That(selector.Conditions[0].Property, Is.EqualTo("IsEnabled"));
            Assert.That(selector.Conditions[0].Value, Is.EqualTo("false"));
        });
    }

    // DataTrigger: active while a {Binding} on the host resolves to a value. The binding runs in producer mode against
    // the host's DataContext; a source-property change re-evaluates and applies/undoes the trigger's setter live. Setter
    // values are strings, as from markup (the setter machinery parses them to the target type).
    [Test]
    public void DataTrigger_AppliesSetter_WhileBoundValueMatches()
    {
        var vm = new ActiveVm { IsActive = false };
        var host = new Border { DataContext = vm };

        var trigger = new DataTrigger { Binding = new Binding("IsActive"), Value = "true" };
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        Assert.That((host.Background as SolidColorBrush)?.Color, Is.Not.EqualTo(Colors.Red), "inactive while the bound value doesn't match");

        vm.IsActive = true;
        Assert.That((host.Background as SolidColorBrush)?.Color, Is.EqualTo(Colors.Red), "applied once the bound value matched");

        vm.IsActive = false;
        Assert.That((host.Background as SolidColorBrush)?.Color, Is.Not.EqualTo(Colors.Red), "undone when the bound value no longer matches");
    }

    // MultiDataTrigger: active only while EVERY binding resolves to its value - the AND of several data triggers.
    [Test]
    public void MultiDataTrigger_AppliesSetter_OnlyWhileAllBoundValuesMatch()
    {
        var vm = new PairVm { Kind = "Fire", IsOn = false };
        var host = new Border { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new Condition { Binding = new Binding(nameof(PairVm.Kind)), Value = "Fire" });
        trigger.Conditions.Add(new Condition { Binding = new Binding(nameof(PairVm.IsOn)), Value = "true" });
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        var oneOfTwo = Red(host);
        vm.IsOn = true;
        var both = Red(host);
        vm.Kind = "Water";
        var otherAgain = Red(host);
        vm.Kind = "Fire";
        var bothAgain = Red(host);

        Assert.Multiple(() =>
        {
            Assert.That(oneOfTwo, Is.False, "one condition of two holds");
            Assert.That(both, Is.True, "both hold");
            Assert.That(otherAgain, Is.False, "undone when the other stops holding");
            Assert.That(bothAgain, Is.True);
        });
    }

    // The tree is built before its data, so the trigger reads the conditions again when the DataContext comes, and again
    // when it is another.
    [Test]
    public void MultiDataTrigger_ReadsItsConditionsAgain_WhenTheDataContextChanges()
    {
        var host = new Border();
        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new Condition { Binding = new Binding(nameof(PairVm.Kind)), Value = "Fire" });
        trigger.Conditions.Add(new Condition { Binding = new Binding(nameof(PairVm.IsOn)), Value = true });
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        var none = Red(host);
        host.DataContext = new PairVm { Kind = "Fire", IsOn = true };
        var matching = Red(host);
        host.DataContext = new PairVm { Kind = "Fire", IsOn = false };
        var other = Red(host);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.False, "no data, nothing holds");
            Assert.That(matching, Is.True, "a typed value is compared as it is");
            Assert.That(other, Is.False);
        });
    }

    // Another DataContext is read whole: a condition already reading the new data and one still reading the old never
    // hold together, so a trigger that holds in neither applies nothing, not for a moment.
    [Test]
    public void MultiDataTrigger_NeverHoldsOnHalfOfAnotherDataContext()
    {
        var host = new Border { DataContext = new PairVm { Kind = "Water", IsOn = true } };
        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new Condition { Binding = new Binding(nameof(PairVm.Kind)), Value = "Fire" });
        trigger.Conditions.Add(new Condition { Binding = new Binding(nameof(PairVm.IsOn)), Value = true });
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));
        var everRed = false;
        host.PropertyChanged += (_, _) => everRed |= Red(host);

        host.DataContext = new PairVm { Kind = "Fire", IsOn = false };

        Assert.That(everRed, Is.False);
    }

    // {Ancestor} is a binding like any other, so a trigger tests it as it tests the data: a property of the element the
    // host stands in.
    [Test]
    public void DataTrigger_TestsAPropertyOfAnAncestor()
    {
        var panel = new Panels.StackPanel { Width = 10 };
        var host = new Border();
        panel.AddLogicalChild(host);

        var trigger = new DataTrigger
        {
            Binding = new Ancestor { AncestorType = typeof(Panels.StackPanel), Path = "Width", Logical = true }, Value = "42"
        };
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        var before = Red(host);
        panel.Width = 42;
        var matching = Red(host);
        panel.Width = 7;

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(matching, Is.True);
            Assert.That(Red(host), Is.False);
        });
    }

    // With no such ancestor, the trigger tests what the binding falls back on.
    [Test]
    public void DataTrigger_OnAnAncestorNotThere_TestsItsFallback()
    {
        var panel = new Panels.StackPanel();
        var host = new Border();
        panel.AddLogicalChild(host);

        var trigger = new DataTrigger
        {
            Binding = new Ancestor { AncestorType = typeof(Panels.Grid), Path = "Width", Logical = true, FallbackValue = "none" },
            Value = "none"
        };
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        Assert.That(Red(host), Is.True);
    }

    // ...and with no FallbackValue, on its TargetNullValue - as the same binding on a property shows.
    [Test]
    public void DataTrigger_OnAnAncestorNotThere_TestsItsTargetNullValue()
    {
        var panel = new Panels.StackPanel();
        var host = new Border();
        panel.AddLogicalChild(host);

        var trigger = new DataTrigger
        {
            Binding = new Ancestor { AncestorType = typeof(Panels.Grid), Path = "Width", Logical = true, TargetNullValue = "none" },
            Value = "none"
        };
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        Assert.That(Red(host), Is.True);
    }

    // {Self} too: a property of the host itself.
    [Test]
    public void DataTrigger_TestsAPropertyOfTheHostItself()
    {
        var host = new Border { Width = 10 };

        var trigger = new DataTrigger { Binding = new Self { Path = "Width" }, Value = "42" };
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        var before = Red(host);
        host.Width = 42;

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(Red(host), Is.True);
        });
    }

    // The data and an ancestor tested together - the host's data names what it is, the ancestor says what it allows -
    // the ancestor found only once the host is placed under it.
    [Test]
    public void MultiDataTrigger_TestsTheDataAndAnAncestorTogether_TheAncestorFoundLater()
    {
        var host = new Border { DataContext = new PairVm { Kind = "Fire" } };
        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new Condition { Binding = new Binding(nameof(PairVm.Kind)), Value = "Fire" });
        trigger.Conditions.Add(new Condition
        {
            Binding = new Ancestor { AncestorType = typeof(Panels.StackPanel), Path = "Width", Logical = true }, Value = "42"
        });
        trigger.Add(new Setter { Property = "Background", Value = "Red" });
        trigger.Apply(new PartContext(host, theme: null));

        var alone = Red(host);
        var panel = new Panels.StackPanel { Width = 42 };
        panel.AddLogicalChild(host);
        var placed = Red(host);
        host.DataContext = new PairVm { Kind = "Water" };
        var otherData = Red(host);

        Assert.Multiple(() =>
        {
            Assert.That(alone, Is.False, "no ancestor yet");
            Assert.That(placed, Is.True, "found once placed under it");
            Assert.That(otherData, Is.False);
        });
    }

    private static bool Red(Border host) => (host.Background as SolidColorBrush)?.Color == Colors.Red;

    private sealed class PairVm : System.ComponentModel.INotifyPropertyChanged
    {
        private string _kind;
        private bool _isOn;

        public string Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Kind)));
            }
        }

        public bool IsOn
        {
            get => _isOn;
            set
            {
                _isOn = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsOn)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // Regression: a trigger/style setter on Width (or Height) that is later CLEARED must not crash. The size callback
    // cast e.NewValue to double, which threw on the UnsetValue a cleared setter produces (guarded now).
    [Test]
    public void PropertyTrigger_ClearingAWidthSetter_DoesNotThrow()
    {
        var host = new Border { IsEnabled = true };
        var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter { Property = "Width", Value = "120" });
        trigger.Apply(new PartContext(host, theme: null));

        host.IsEnabled = false;
        Assert.That(host.Width, Is.EqualTo(120.0), "the trigger applied its Width");

        Assert.DoesNotThrow(() => host.IsEnabled = true, "clearing the Width setter must not (double)UnsetValue-throw");
        Assert.That(host.Width, Is.Not.EqualTo(120.0), "Width reverted to its base once the trigger let go");
    }

    // ZIndex is a real AdamantiumProperty, so styles/triggers/AUML can set it by name (what makes the render + hit-test
    // paint order themeable). A raised child draws over its siblings (composited by ZIndex in RenderCache).
    [Test]
    public void ZIndex_IsARegisteredAdamantiumProperty_SettableByName()
    {
        var host = new Border();
        Assert.That(host.ZIndex, Is.EqualTo(0), "default document order");
        Assert.That(host.GetProperty("ZIndex"), Is.Not.Null, "registered DP");

        host.SetValue("ZIndex", 3, ValuePriority.Local);
        Assert.That(host.ZIndex, Is.EqualTo(3), "settable by name (the style/trigger/AUML path)");
    }

    // Re-applying a style without a preceding detach (the theme-swap path) must not pile up activators, and Detach must
    // fully remove the style's own activators (no leaked, still-subscribed trigger). Exercises Style's per-component
    // activator tracking (RemoveMyActivators / RecordActivator).
    [Test]
    public void Style_ReAttachThenDetach_AppliesOnceAndFullyDetaches()
    {
        var host = new Border();
        var style = new Style();
        style.Selector.Types.Add(typeof(Border));
        var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter { Property = "Width", Value = "50" });
        style.Triggers.Add(trigger);

        style.Attach(host);
        style.Attach(host);   // re-apply without detach
        host.IsEnabled = false;
        Assert.That(host.Width, Is.EqualTo(50.0), "re-attached style still applies its trigger");

        style.Detach(host);
        host.IsEnabled = true;
        host.IsEnabled = false;
        Assert.That(host.Width, Is.Not.EqualTo(50.0), "fully detached - the activator is gone, not left subscribed");
    }

    // Two triggers give the same class, and a style matched by it gives one more: each stays while anything gives it,
    // and all go when the triggers do - in either order.
    [TestCase(true)]
    [TestCase(false)]
    public void Triggers_GivingTheSameClass_TakeItBackWithTheLast(bool firstLeavesFirst)
    {
        var vm = new ActiveVm();
        var button = new Button { DataContext = vm };
        var marked = new Style();
        marked.Selector.Types.Add(typeof(Button));
        marked.Selector.Classes.Add("Accent");
        marked.Setters.Add(new Setter("Classes", "Marked"));
        button.Styles.Add(marked);

        var byData = new DataTrigger { Binding = new Binding("IsActive"), Value = "true" };
        byData.Add(new Setter("Classes", "Accent"));
        var byState = new PropertyTrigger { Property = "IsEnabled", Value = false };
        byState.Add(new Setter("Classes", "Accent"));
        var style = new Style();
        style.Selector.Types.Add(typeof(Button));
        style.Triggers.Add(byData);
        style.Triggers.Add(byState);
        button.AttachStyles(style);

        vm.IsActive = true;
        button.IsEnabled = false;
        var both = button.ClassNames.ToArray();
        if (firstLeavesFirst) vm.IsActive = false; else button.IsEnabled = true;
        var one = button.ClassNames.ToArray();
        if (firstLeavesFirst) button.IsEnabled = true; else vm.IsActive = false;

        Assert.Multiple(() =>
        {
            Assert.That(both, Is.EquivalentTo(new[] { "Accent", "Marked" }));
            Assert.That(one, Is.EquivalentTo(new[] { "Accent", "Marked" }), "the other trigger still gives Accent");
            Assert.That(button.ClassNames, Is.Empty, "the last giver gone, its class and what it brought gone");
        });
    }

    // A trigger taking back a theme class takes off the theme's styles of it, and a style that class brought, whose
    // classes going restyle the button again under the first restyle.
    [Test]
    public void Triggers_TakingBackAThemeClass_TakeOffWhatItBrought()
    {
        var vm = new ActiveVm();
        var button = new Button { DataContext = vm };
        var set = new Adamantium.UI.Themes.FluentTheme.ButtonStyleSet();
        set.Initialize(null);
        foreach (var themed in set.Styles) button.Styles.Add(themed);
        var marked = new Style();
        marked.Selector.Types.Add(typeof(Button));
        marked.Selector.Classes.Add("Accent");
        marked.Setters.Add(new Setter("Classes", "Marked"));
        button.Styles.Add(marked);
        var trigger = new DataTrigger { Binding = new Binding("IsActive"), Value = "true" };
        trigger.Add(new Setter("Classes", "Accent"));
        var giving = new Style();
        giving.Selector.Types.Add(typeof(Button));
        giving.Triggers.Add(trigger);
        button.Styles.Add(giving);
        var root = new Border { Width = 400, Height = 100, Child = button };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);

        vm.IsActive = true;
        var on = button.ClassNames.ToArray();

        Assert.DoesNotThrow(() => vm.IsActive = false);
        Assert.Multiple(() =>
        {
            Assert.That(on, Is.EquivalentTo(new[] { "Accent", "Marked" }));
            Assert.That(button.ClassNames, Is.Empty);
        });
    }

    // A style whose trigger gives a class as the style attaches restyles inside its own attaching: it is attached once,
    // with one activator for its trigger.
    [Test]
    public void AStyle_GivingAClassAsItAttaches_IsAttachedOnce()
    {
        var vm = new ActiveVm { IsActive = true };
        var button = new Button { DataContext = vm };
        var trigger = new DataTrigger { Binding = new Binding("IsActive"), Value = "true" };
        trigger.Add(new Setter("Classes", "Accent"));
        var giving = new Style();
        giving.Selector.Types.Add(typeof(Button));
        giving.Triggers.Add(trigger);
        button.Styles.Add(giving);

        button.AttachStyles(giving);

        Assert.Multiple(() =>
        {
            Assert.That(button.ClassNames, Is.EqualTo(new[] { "Accent" }));
            Assert.That(button.StyleActivators, Has.Count.EqualTo(1));
        });
    }

    private sealed class ActiveVm : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // A trigger must be able to set an ATTACHED property, exactly as a style setter can: the property is named in markup
    // (`Grid.Row`), and resolving that name is one job with one answer. It was two - the style path read the owner off the
    // dotted name and the trigger path did not - so a trigger that moved a part to another grid cell silently did nothing
    // (the folded tab strip stayed under the body instead of beside it), and clearing it did nothing either.
    [Test]
    public void PropertyTrigger_CanSetAndClearAnAttachedProperty()
    {
        var host = new Border { Child = new Border() };
        var part = (Border)host.Child;

        var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter { Property = "Grid.Row", Value = "2" });
        trigger.Apply(new FixedTargetContext(host, part));

        host.IsEnabled = false;
        Assert.That(Panels.Grid.GetRow(part), Is.EqualTo(2), "the trigger moved the part to another row");

        host.IsEnabled = true;
        Assert.That(Panels.Grid.GetRow(part), Is.EqualTo(0), "and leaving the trigger put it back");
    }

    // A context whose every TargetName resolves to one given part - the trigger machinery only needs FindTarget, and a
    // real template would drag in an items/parts setup this question does not depend on.
    private sealed class FixedTargetContext : ITriggerExecutionContext
    {
        private readonly IFundamentalUIComponent _host;
        private readonly IAdamantiumComponent _target;

        public FixedTargetContext(IFundamentalUIComponent host, IAdamantiumComponent target)
        {
            _host = host;
            _target = target;
        }

        public IFundamentalUIComponent HostComponent => _host;
        public ITheme Theme => null;
        public IAdamantiumComponent FindTarget(string targetName) => _target;
    }

    // A trigger can only be carried by a STYLE, and a style is selected by control type - so a group living inside a
    // template had no way to answer for itself and the question had to be widened to the whole control (a caption's
    // traffic lights lit from anywhere on the title bar). SourceName is the read-side mirror of Setter.TargetName.
    [Test]
    public void PropertyTrigger_SourceName_ReadsThePartAndNotTheHost()
    {
        var host = new Button();
        host.Template = TwoPartTemplate("Group", "Glyph");

        // Not IsEnabled: a disabled host disables its parts too, so it could not tell the two apart.
        var trigger = new PropertyTrigger { SourceName = "Group", Property = "IsHitTestVisible", Value = false };
        trigger.Add(new Setter { TargetName = "Glyph", Property = "Opacity", Value = "0.5" });
        trigger.Apply(new PartContext(host, theme: null));

        var group = (Border)host.GetTemplateChild("Group");
        var glyph = (Border)host.GetTemplateChild("Glyph");
        Assert.That(glyph.Opacity, Is.EqualTo(1).Within(0.001), "inactive before the condition holds");

        host.IsHitTestVisible = false;
        Assert.That(glyph.Opacity, Is.EqualTo(1).Within(0.001),
            "the HOST's own property must not answer for a trigger that names a source");

        host.IsHitTestVisible = true;
        group.IsHitTestVisible = false;
        Assert.That(glyph.Opacity, Is.EqualTo(0.5).Within(0.001), "the named part is what the trigger watches");

        group.IsHitTestVisible = true;
        Assert.That(glyph.Opacity, Is.EqualTo(1).Within(0.001), "...and it lets go when the part goes back");
    }

    // A trigger that READS a part reaches into the template just as surely as one that writes to it: a swap discards the
    // part it listens to, and an activator still subscribed to the old one would never hear anything again.
    [Test]
    public void PropertyTrigger_SourceName_FollowsATemplateSwap()
    {
        var button = new Button();

        var style = new Style();
        style.Selector.Types.Add(typeof(Button));
        var trigger = new PropertyTrigger { SourceName = "Group", Property = "IsEnabled", Value = false };
        trigger.Add(new Setter { TargetName = "Glyph", Property = "Opacity", Value = "0.5" });
        style.Triggers.Add(trigger);
        style.Attach(button);

        button.Template = TwoPartTemplate("Group", "Glyph");
        var groupA = (Border)button.GetTemplateChild("Group");

        button.Template = TwoPartTemplate("Group", "Glyph");
        var groupB = (Border)button.GetTemplateChild("Group");
        var glyphB = (Border)button.GetTemplateChild("Glyph");
        Assert.That(groupB, Is.Not.SameAs(groupA), "a genuinely new part instance");

        groupA.IsEnabled = false;
        Assert.That(glyphB.Opacity, Is.EqualTo(1).Within(0.001), "the discarded part no longer drives anything");

        groupB.IsEnabled = false;
        Assert.That(glyphB.Opacity, Is.EqualTo(0.5).Within(0.001), "re-pointed onto the new template's source");
    }

    private static ControlTemplate TwoPartTemplate(string sourceName, string targetName)
    {
        var source = new Border();
        var target = new Border();
        source.Child = target;
        return new ControlTemplate(() =>
        {
            var result = new TemplateResult();
            result.RegisterName(sourceName, source);
            result.RegisterName(targetName, target);
            result.RootComponent = source;
            return result;
        });
    }

    private static ControlTemplate NamedPartTemplate(string partName)
    {
        var inner = new Border();
        return new ControlTemplate(() =>
        {
            var result = new TemplateResult();
            result.RegisterName(partName, inner);
            result.RootComponent = inner;
            return result;
        });
    }

    // Mirrors StyleTriggerExecutionContext (internal): the host for an empty TargetName, otherwise a named part of the
    // host's template - the path that lets a standalone "triggers" style reach the parts a ControlTemplate defines.
    private sealed class PartContext : ITriggerExecutionContext
    {
        private readonly IFundamentalUIComponent _host;
        public PartContext(IFundamentalUIComponent host, ITheme theme) { _host = host; Theme = theme; }
        public IFundamentalUIComponent HostComponent => _host;
        public ITheme Theme { get; }
        public IAdamantiumComponent FindTarget(string targetName) =>
            string.IsNullOrEmpty(targetName) ? _host : (_host as ITemplatedUIComponent)?.GetTemplateChild(targetName);
    }
}
