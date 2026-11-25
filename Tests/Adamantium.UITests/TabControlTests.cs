using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Core.TypeParsers;
using NUnit.Framework;

namespace Adamantium.UITests;

// TabControl single-selection: the first tab is auto-selected, the selected tab's body surfaces as SelectedContent, and
// selection is reflected onto the realized TabItem containers' IsSelected. Containers are realized directly via the
// generator (no GPU/layout needed) - the initial on-attach highlight of own-container tabs is left to a visual run.
public class TabControlTests
{
    private static TabControl WithTabs(params (string header, string body)[] tabs)
    {
        var tc = new TabControl();
        foreach (var (h, b) in tabs)
            tc.Items.Add(new TabItem { Header = h, Content = b });
        return tc;
    }

    // The order a real data-bound strip is built in: the SelectedItem binding is established while the ItemsSource has
    // not produced anything yet, so the view-model names its tab against an EMPTY collection. That name must survive
    // until the items arrive - it was discarded on the spot, and because the binding is TwoWay the discard went back to
    // the source, so the app opened on whatever tab happened to be first instead of the one asked for.
    [Test]
    public void ATabNamedBeforeTheItemsArrive_IsTheOneSelected()
    {
        var vm = new TabsVm();
        vm.Tabs.Add("A");
        vm.Tabs.Add("B");
        vm.Tabs.Add("C");
        vm.Selected = "C";

        var tc = new TabControl { DataContext = vm };
        tc.SetBinding("SelectedItem", new Binding("Selected") { Mode = BindingMode.TwoWay });
        tc.SetBinding("ItemsSource", new Binding("Tabs"));

        Assert.Multiple(() =>
        {
            Assert.That(tc.SelectedItem, Is.EqualTo("C"), "the strip opened on the tab the source named");
            Assert.That(tc.SelectedIndex, Is.EqualTo(2));
            Assert.That(vm.Selected, Is.EqualTo("C"), "and the source was never overwritten with the first tab");
        });
    }

    // The other order markup can produce - ItemsSource first, so the strip has already auto-selected its first tab by the
    // time the SelectedItem binding is established. The binding must still win: the control's own housekeeping wrote at
    // Local priority, which outranks a binding, and the source's choice could then never take effect at all.
    [Test]
    public void ASelectionBinding_OverrulesTheAutoSelectedFirstTab()
    {
        var vm = new TabsVm();
        vm.Tabs.Add("A");
        vm.Tabs.Add("B");
        vm.Tabs.Add("C");
        vm.Selected = "C";

        var tc = new TabControl { DataContext = vm };
        tc.SetBinding("ItemsSource", new Binding("Tabs"));
        Assert.That(tc.SelectedItem, Is.EqualTo("A"), "nothing stated yet -> the first tab, as ever");

        tc.SetBinding("SelectedItem", new Binding("Selected") { Mode = BindingMode.TwoWay });

        Assert.Multiple(() =>
        {
            Assert.That(tc.SelectedItem, Is.EqualTo("C"), "the binding overrules the default selection");
            Assert.That(tc.SelectedIndex, Is.EqualTo(2));
        });
    }

    private sealed class TabsVm
    {
        public System.Collections.ObjectModel.ObservableCollection<string> Tabs { get; } = new();
        public string Selected { get; set; }
    }

    private sealed class ParkedView : Border
    {
        public override NavigationCacheMode KeepAlive => NavigationCacheMode.Required;
    }

    // Tabs A to F bound through a view that shows B, D and F, D selected.
    private static (TabControl Tabs, TabsVm Vm, CollectionView View) Filtered()
    {
        var vm = new TabsVm();
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F" }) vm.Tabs.Add(name);
        var view = new CollectionView(vm.Tabs);
        var tabs = new TabControl { ItemsSource = view, SelectedItem = "D" };
        view.Filter = tab => tab is "B" or "D" or "F";
        return (tabs, vm, view);
    }

    [Test]
    public void AFilter_KeepsTheSelectedTabSelected_AndTheHiddenTabsOpen()
    {
        var (tabs, _, view) = Filtered();

        Assert.Multiple(() =>
        {
            Assert.That(tabs.Items, Is.EqualTo(new[] { "B", "D", "F" }));
            Assert.That(tabs.SelectedItem, Is.EqualTo("D"));
            Assert.That(tabs.HoldsContent("A"), Is.True, "hidden, its page is not let go of");
            Assert.That(tabs.HoldsContent("none"), Is.False);
        });

        view.Filter = null;

        Assert.That(tabs.SelectedItem, Is.EqualTo("D"));
        Assert.That(tabs.SelectedIndex, Is.EqualTo(3));
    }

    [TestCase("F")]
    [TestCase("none")]
    public void ASelectedTabAFilterHides_StaysSelected_AndComesBackWithIt(string shown)
    {
        var (tabs, _, view) = Filtered();
        var changes = 0;
        tabs.SelectionChanged += (_, _) => changes++;

        view.Filter = tab => tab is string name && name == shown;

        Assert.Multiple(() =>
        {
            Assert.That(tabs.SelectedItem, Is.EqualTo("D"), "still the one read");
            Assert.That(tabs.SelectedContent, Is.EqualTo("D"));
            Assert.That(tabs.SelectedIndex, Is.EqualTo(-1), "it has no place in the strip");
        });

        view.Filter = null;

        Assert.That(tabs.SelectedIndex, Is.EqualTo(3));
        Assert.That(changes, Is.Zero, "nothing else was opened on the way");
    }

    [Test]
    public void ASelectedHiddenTab_ClosedElsewhere_GivesTheSelectionUp()
    {
        var (tabs, vm, view) = Filtered();
        view.Filter = tab => tab is "F";

        vm.Tabs.Remove("D");

        Assert.That(tabs.SelectedItem, Is.EqualTo("F"));
    }

    [Test]
    public void AHiddenTabSelectedFromOutside_ShowsItsPage()
    {
        var (tabs, _, view) = Filtered();

        tabs.SelectedItem = "A";

        Assert.That(tabs.SelectedContent, Is.EqualTo("A"));
        view.Filter = null;
        Assert.That(tabs.SelectedIndex, Is.Zero);
    }

    [Test]
    public void ATabClosedThroughAView_LeavesTheListItLooksAt()
    {
        var (tabs, vm, _) = Filtered();

        tabs.RequestCloseItem("F");

        Assert.That(vm.Tabs, Is.EqualTo(new[] { "A", "B", "C", "D", "E" }));
        Assert.That(tabs.Items, Is.EqualTo(new[] { "B", "D" }));
        Assert.That(tabs.HoldsContent("F"), Is.False, "closed for good");
    }

    [Test]
    public void AHiddenTab_KeepsItsPage_UntilItLeavesTheList()
    {
        ParkedVisuals.Clear();
        var (tabs, vm, view) = Filtered();
        view.Filter = null;
        ParkedVisuals.Keep(tabs, "A", new ParkedView());

        view.Filter = tab => tab is "B" or "D";
        view.Refresh();
        Assert.That(ParkedVisuals.TryTake(tabs, "A", null, out var kept, out _, out _, out _), Is.True, "hidden, kept");

        ParkedVisuals.Keep(tabs, "A", kept);
        vm.Tabs.Remove("A");
        Assert.That(ParkedVisuals.TryTake(tabs, "A", null, out _, out _, out _, out _), Is.False, "closed, let go of");
        ParkedVisuals.Clear();
    }

    [Test]
    public void OfEqualTabs_TheOneDraggedIsTheOneMoved()
    {
        var first = new string("same");
        var second = new string("same");
        var vm = new TabsVm();
        vm.Tabs.Add(first);
        vm.Tabs.Add("other");
        vm.Tabs.Add(second);
        var tabs = new TabControl { ItemsSource = new CollectionView(vm.Tabs) };

        tabs.MoveItem(2, 1);

        Assert.That(vm.Tabs[0], Is.SameAs(first));
        Assert.That(vm.Tabs[1], Is.SameAs(second));
    }

    [Test]
    public void ASortedView_KeepsTheOrderOfTheListItLooksAt()
    {
        var (tabs, vm, view) = Filtered();
        view.SortDescriptions.Add(new SortDescription("Length", SortDirection.Ascending));

        tabs.MoveItem(0, 2);

        Assert.That(string.Join(",", vm.Tabs), Is.EqualTo("A,B,C,D,E,F"));
    }

    // A tab that is itself the page: shown, hidden by a filter and another chosen, it is not thrown away.
    [Test]
    public void AHiddenTabThatIsItsOwnPage_IsNotDiscarded()
    {
        var first = new Border();
        var second = new Border();
        var view = new CollectionView(new TrackingCollection<object>([first, second]));
        ContentPresenter presenter = null;
        var tabs = new TabControl
        {
            ItemsSource = view,
            Template = new ControlTemplate(() =>
            {
                presenter = new ContentPresenter();
                var result = new TemplateResult { RootComponent = presenter };
                result.AddTemplateBinding(presenter, "Content", new TemplateBinding { Path = "SelectedContent" });
                return result;
            })
        };
        tabs.Measure(new Size<double>(100, 100));
        Assume.That(((IUIComponent)first).VisualParent, Is.SameAs(presenter));

        view.Filter = item => ReferenceEquals(item, second);
        tabs.SelectedItem = second;
        tabs.Measure(new Size<double>(101, 100));
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(((IUIComponent)second).VisualParent, Is.SameAs(presenter));
        Assert.That(first.IsDiscarded, Is.False);
    }

    [Test]
    public void AHiddenTabNamedByItsValue_ShowsItsPage()
    {
        var (tabs, _, view) = Filtered();

        tabs.SelectedValue = "A";

        Assert.That(tabs.SelectedContent, Is.EqualTo("A"));
        Assert.That(tabs.SelectedValue, Is.EqualTo("A"));
        view.Filter = null;
        Assert.That(tabs.SelectedIndex, Is.Zero);
    }

    // The bound list tells its listeners one by one: changed by the cap before the last heard of the tab added, it told
    // that one of the closing first.
    [Test]
    public void ATabOpenedPastTheCap_OfAListOthersFollowToo_ClosesTheOldest()
    {
        var list = new TrackingCollection<string>(["A", "B", "C"]);
        var tabs = new TabControl { ItemsSource = new CollectionView(list), MaxOpenedTabs = 3, SelectedItem = "C" };
        var other = new CollectionView(list);

        Assert.DoesNotThrow(() => list.Add("D"));
        LoopSignal.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "B", "C", "D" }));
            Assert.That(other, Is.EqualTo(list));
            Assert.That(tabs.Items, Is.EqualTo(list));
        });
    }

    [Test]
    public void ATabPutBackAsTheCapClosesIt_DoesNotHoldTheLoop()
    {
        var list = new TrackingCollection<string>(["A", "B", "C"]);
        var tabs = new TabControl { ItemsSource = list, MaxOpenedTabs = 3, SelectedItem = "C" };
        var closed = 0;
        list.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is [string gone] && ++closed < 100) list.Add(gone);
        };

        list.Add("D");
        LoopSignal.Drain();

        Assert.That(closed, Is.LessThan(100));
        Assert.That(tabs.Items, Is.EqualTo(list));
    }

    [Test]
    public void TabsOfATrackingCollection_AreClosedAndMoved()
    {
        var list = new TrackingCollection<string>(["A", "B", "C"]);
        var tabs = new TabControl { ItemsSource = list };

        tabs.RequestCloseItem("B");
        tabs.MoveItem(0, 1);

        Assert.That(list, Is.EqualTo(new[] { "C", "A" }));
    }

    [Test]
    public void SeveralTabsAddedAtOncePastTheCap_LeaveTheViewAndItsListAgreeing()
    {
        var list = new TrackingCollection<string>(["A", "B"]);
        var tabs = new TabControl { ItemsSource = new CollectionView(list), MaxOpenedTabs = 3 };

        Assert.DoesNotThrow(() => list.AddRange(["C", "D", "E"]));
        LoopSignal.Drain();
        Assert.That(list, Has.Count.EqualTo(3));
        Assert.That(tabs.Items, Is.EqualTo(list));
    }

    [Test]
    public void ATabTheCapClosesAsAnotherCloses_IsTheOldest()
    {
        var vm = new TabsVm();
        foreach (var name in new[] { "A", "B", "C", "D" }) vm.Tabs.Add(name);
        var tabs = new TabControl { ItemsSource = new CollectionView(vm.Tabs), SelectedItem = "D" };
        var keep = true;
        tabs.TabCloseRequested += (_, e) => e.Cancel = keep;
        tabs.MaxOpenedTabs = 2;
        keep = false;

        tabs.RequestCloseItem("A");
        LoopSignal.Drain();

        Assert.That(vm.Tabs, Is.EqualTo(new[] { "C", "D" }));
        Assert.That(tabs.Items, Is.EqualTo(new[] { "C", "D" }));
    }

    [Test]
    public void AHiddenTabOpenedPastTheCap_ClosesTheOldestAtOnce()
    {
        var (tabs, vm, _) = Filtered();
        tabs.MaxOpenedTabs = 6;

        vm.Tabs.Add("hidden");
        LoopSignal.Drain();

        Assert.That(string.Join(",", vm.Tabs), Is.EqualTo("B,C,D,E,F,hidden"));
    }

    [Test]
    public void ATabMoved_IsNoYoungerUnderTheCap()
    {
        var (tabs, vm, view) = Filtered();
        view.Filter = null;
        tabs.MaxOpenedTabs = 6;
        tabs.MoveItem(0, 1);

        vm.Tabs.Add("G");
        LoopSignal.Drain();

        Assert.That(vm.Tabs, Does.Not.Contain("A"));
        Assert.That(vm.Tabs, Does.Contain("B"));
    }

    [Test]
    public void TheCapCountsTheHiddenTabsToo()
    {
        var (tabs, vm, _) = Filtered();

        tabs.MaxOpenedTabs = 4;

        Assert.That(string.Join(",", vm.Tabs), Is.EqualTo("C,D,E,F"), "the oldest go, hidden or not");
    }

    [TestCase(0, 2, "A,C,D,E,F,B")]
    [TestCase(2, 0, "A,F,B,C,D,E")]
    [TestCase(1, 0, "A,D,B,C,E,F")]
    public void ATabMovedThroughAView_StandsBesideTheOneItWasDroppedAt(int from, int to, string expected)
    {
        var (tabs, vm, _) = Filtered();

        tabs.MoveItem(from, to);

        Assert.That(string.Join(",", vm.Tabs), Is.EqualTo(expected));
        Assert.That(tabs.SelectedItem, Is.EqualTo("D"));
    }

    [TestCase(0, "A,X,B,C,D,E,F")]
    [TestCase(2, "A,B,C,D,E,X,F")]
    [TestCase(3, "A,B,C,D,E,F,X")]
    public void ATabCarriedIntoAView_StandsBeforeTheOneShownThere(int index, string expected)
    {
        var (tabs, vm, _) = Filtered();

        tabs.InsertIntoSource(index, "X");

        Assert.That(string.Join(",", vm.Tabs), Is.EqualTo(expected));
    }

    [Test]
    public void FirstTab_IsAutoSelected()
    {
        var tc = WithTabs(("A", "a"), ("B", "b"));
        Assert.Multiple(() =>
        {
            Assert.That(tc.SelectedIndex, Is.EqualTo(0));
            Assert.That(((TabItem)tc.SelectedItem).Header, Is.EqualTo("A"));
            Assert.That(tc.SelectedContent, Is.EqualTo("a"));
        });
    }

    // A tab put in before the selected one - a torn-off tab carried back - moves the selected one's index, not the
    // selection: keeping the index selected its neighbour.
    [Test]
    public void ATabInsertedBeforeTheSelectedOne_LeavesTheSelectionOnIt()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "b", "c", "d" };
        var tc = new TabControl { ItemsSource = items };
        tc.SelectedItem = "c";

        items.Insert(1, "x");

        Assert.Multiple(() =>
        {
            Assert.That(tc.SelectedItem, Is.EqualTo("c"));
            Assert.That(tc.SelectedIndex, Is.EqualTo(3));
        });
    }

    [Test]
    public void SelectedIndex_SurfacesThatTabsBody_AsSelectedContent()
    {
        var tc = WithTabs(("A", "a"), ("B", "b"), ("C", "c"));
        tc.SelectedIndex = 2;
        Assert.That(tc.SelectedContent, Is.EqualTo("c"));
    }

    [Test]
    public void Selection_ReflectsOntoRealizedTabItems()
    {
        var tc = WithTabs(("A", "a"), ("B", "b"), ("C", "c"));
        var g = tc.ItemContainerGenerator;
        var t0 = (TabItem)g.Realize(0);
        var t1 = (TabItem)g.Realize(1);
        var t2 = (TabItem)g.Realize(2);

        tc.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(t0.IsSelected, Is.False);
            Assert.That(t1.IsSelected, Is.True);
            Assert.That(t2.IsSelected, Is.False);
        });
    }

    [Test]
    public void SelectTab_SelectsThatContainer()
    {
        var tc = WithTabs(("A", "a"), ("B", "b"), ("C", "c"));
        var t2 = (TabItem)tc.ItemContainerGenerator.Realize(2);

        tc.SelectTab(t2);   // simulates a click on the tab header

        Assert.Multiple(() =>
        {
            Assert.That(tc.SelectedIndex, Is.EqualTo(2));
            Assert.That(tc.SelectedContent, Is.EqualTo("c"));
            Assert.That(t2.IsSelected, Is.True);
        });
    }

    [Test]
    public void DataItems_AreWrappedInTabItems_HeaderAndBodyAreTheItem()
    {
        var tc = new TabControl { ItemsSource = new[] { "x", "y" } };
        var t0 = (TabItem)tc.ItemContainerGenerator.Realize(0);

        Assert.Multiple(() =>
        {
            Assert.That(tc.SelectedIndex, Is.EqualTo(0));
            Assert.That(t0.Header, Is.EqualTo("x"));
            Assert.That(tc.SelectedContent, Is.EqualTo("x"));
        });
    }

    // A TabStripPlacement trigger swaps the template at Trigger priority and reverts cleanly, without recursing through
    // template-change trigger re-evaluation.
    [Test]
    public void TabStripPlacement_SelectsTemplateViaTrigger()
    {
        var tc = new TabControl();
        tc.Items.Add(new TabItem { Header = "A", Content = "a" });

        var style = new Style();
        style.Selector.Types.Add(typeof(TabControl));
        style.Setters.Add(new Setter { Property = "Template", Value = PartTemplate("PART_Top") });   // base (Top)
        var left = new PropertyTrigger { Property = "TabStripPlacement", Value = "Left" };
        left.Add(new Setter { Property = "Template", Value = PartTemplate("PART_Left") });
        style.Triggers.Add(left);
        style.Attach(tc);

        Assert.That(tc.GetTemplateChild("PART_Top"), Is.Not.Null, "base template active at the default Top placement");

        tc.TabStripPlacement = TabStripPlacement.Left;
        Assert.Multiple(() =>
        {
            Assert.That(tc.TabStripPlacement, Is.EqualTo(TabStripPlacement.Left));
            Assert.That(tc.GetTemplateChild("PART_Left"), Is.Not.Null, "Left placement swapped in its own template");
            Assert.That(tc.GetTemplateChild("PART_Top"), Is.Null, "the base template is gone, not stacked under the new one");
        });

        tc.TabStripPlacement = TabStripPlacement.Top;
        Assert.Multiple(() =>
        {
            Assert.That(tc.GetTemplateChild("PART_Top"), Is.Not.Null, "reverts to the base template once the trigger no longer holds");
            Assert.That(tc.GetTemplateChild("PART_Left"), Is.Null, "the placement template is fully torn down on exit");
        });
    }

    // The mechanism the THEME actually uses: a property-condition SELECTOR ("TabControl[TabStripPlacement=Left]") is its
    // own small style; it attaches to every TabControl (structurally) and applies its template only WHILE the placement
    // matches, switching as the property changes. Exercises the selector parser + StyleSelector.Conditions + the
    // conditioned-setter activation in Style.Attach end to end.
    [Test]
    public void TabStripPlacement_SelectsTemplateViaConditionalSelector()
    {
        var tc = new TabControl();
        tc.Items.Add(new TabItem { Header = "A", Content = "a" });

        var baseStyle = new Style();
        baseStyle.Selector.Types.Add(typeof(TabControl));
        baseStyle.Setters.Add(new Setter { Property = "Template", Value = PartTemplate("PART_Top") });
        baseStyle.Attach(tc);

        var leftStyle = new Style { Selector = new SelectorParser().Parse("TabControl[TabStripPlacement=Left]") };
        leftStyle.Setters.Add(new Setter { Property = "Template", Value = PartTemplate("PART_Left") });
        leftStyle.Attach(tc);   // attaches structurally even though the condition is unmet right now

        Assert.That(tc.GetTemplateChild("PART_Top"), Is.Not.Null, "base template while the placement condition is unmet");

        tc.TabStripPlacement = TabStripPlacement.Left;
        Assert.Multiple(() =>
        {
            Assert.That(tc.GetTemplateChild("PART_Left"), Is.Not.Null, "the matching placement style swapped its template in");
            Assert.That(tc.GetTemplateChild("PART_Top"), Is.Null, "base template is overridden, not stacked");
        });

        tc.TabStripPlacement = TabStripPlacement.Top;
        Assert.That(tc.GetTemplateChild("PART_Top"), Is.Not.Null, "reverts to the base template once the condition no longer holds");
    }

    // Pluggable tabs from a collection of view-models: a TabControl's ItemTemplate is its HEADER template (WPF
    // semantics). It must flow onto each generated tab's HeaderTemplate (and ItemTemplateSelector -> HeaderTemplateSelector)
    // so a data header renders through the template instead of ToString(). The body uses ContentTemplate/Selector instead.
    [Test]
    public void DataBound_ItemTemplateFlowsToTabItemHeaderTemplate()
    {
        var headerTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border() });
        var selector = new DummySelector();
        var items = new[] { new PageVm { Title = "One" }, new PageVm { Title = "Two" } };
        var tc = new TabControl { ItemsSource = items, ItemTemplate = headerTemplate, ItemTemplateSelector = selector };

        var t0 = (TabItem)tc.ItemContainerGenerator.Realize(0);

        Assert.Multiple(() =>
        {
            Assert.That(t0.Header, Is.SameAs(items[0]), "the data item is the header");
            Assert.That(t0.HeaderTemplate, Is.SameAs(headerTemplate), "TabControl.ItemTemplate flows onto the tab's HeaderTemplate");
            Assert.That(t0.HeaderTemplateSelector, Is.SameAs(selector), "and ItemTemplateSelector -> HeaderTemplateSelector");
        });
    }

    private sealed class PageVm { public string Title { get; init; } }

    private sealed class HeaderVm { public string Header { get; init; } }

    private sealed class DummySelector : DataTemplateSelector { }

    // Reproduces the REAL header path: a TabItem whose template mirrors the theme (a PART_ContentPresenter with
    // Content={TemplateBinding Header} + ContentTemplate={TemplateBinding HeaderTemplate}), with DataContext/Header/
    // HeaderTemplate set exactly as TabControl.PrepareContainer sets them for a data item. The header template's
    // {Binding Header} must resolve against the tab's DataContext (the item view-model) - i.e. render its label, not empty.
    [Test]
    public void TabItem_HeaderTemplate_BindsHeaderTextAgainstItemDataContext()
    {
        var vm = new HeaderVm { Header = "Buttons" };
        var headerTemplate = new DataTemplate(() =>
        {
            var tb = new TextBlock();
            var r = new TemplateResult { RootComponent = tb };
            tb.SetBinding("Text", new Binding("Header"));
            return r;
        });

        var tab = new TabItem { DataContext = vm, Header = vm, HeaderTemplate = headerTemplate };
        tab.Template = new ControlTemplate(() =>
        {
            var cp = new ContentPresenter();
            var r = new TemplateResult { RootComponent = cp };
            r.RegisterName("PART_ContentPresenter", cp);
            r.AddTemplateBinding(cp, "Content", new TemplateBinding { Path = "Header" });
            r.AddTemplateBinding(cp, "ContentTemplate", new TemplateBinding { Path = "HeaderTemplate" });
            return r;
        });
        tab.Measure(new Size<double>(200, 50));
        tab.Arrange(new Rect(0, 0, 200, 50));

        var presenter = (ContentPresenter)tab.GetTemplateChild("PART_ContentPresenter");
        var tbNode = presenter.VisualChildren.OfType<TextBlock>().FirstOrDefault();
        Assert.Multiple(() =>
        {
            // Regression: the presenter's ContentTemplate change routes through the same handler as its Content change, so
            // the DataContext rule wrongly adopted the DataTemplate (not the item) - headers/bodies then bound to nothing.
            Assert.That(presenter.DataContext, Is.SameAs(vm), "presenter adopts the item VM (not the header template) as DataContext");
            Assert.That(tbNode, Is.Not.Null, "header template built its TextBlock");
            Assert.That(tbNode?.DataContext, Is.SameAs(vm), "the built TextBlock inherits the item VM");
            Assert.That(tbNode?.Text, Is.EqualTo("Buttons"), "the header template's {Binding Header} resolves against the item VM");
        });
    }

    private static ControlTemplate PartTemplate(string partName) => new(() =>
    {
        var root = new Border();
        var result = new TemplateResult { RootComponent = root };
        result.RegisterName(partName, root);
        return result;
    });
}
