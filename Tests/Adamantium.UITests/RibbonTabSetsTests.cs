using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The modules of a document bring their own tabs: a piece of each module's view, bound against the module,
/// under a ledge of its own - there while the module is in the document, gone with it.</summary>
[TestFixture]
public class RibbonTabSetsTests
{
    private sealed class Module : INotifyPropertyChanged
    {
        private bool _isShown = true;

        public string Name { get; init; }

        public bool IsShown
        {
            get => _isShown;
            set
            {
                _isShown = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // As a module's markup writes it: <RibbonTabSet IsActive="{Binding IsShown}"><RibbonTab Header="{Binding Name}"/>.
    private static DataTemplate ModuleTabs() => new(() =>
    {
        var set = new RibbonTabSet();
        set.SetBinding(nameof(RibbonTabSet.IsActive), new Binding(nameof(Module.IsShown)) { IsImmediate = true });
        var tab = new RibbonTab();
        tab.SetBinding(nameof(RibbonTab.Header), new Binding(nameof(Module.Name)) { IsImmediate = true });
        set.AddOrSetChildComponent(tab);
        return new TemplateResult { RootComponent = set };
    });

    private static RibbonTab TabOf(Ribbon ribbon, Module module) =>
        ribbon.Items.OfType<RibbonTab>().FirstOrDefault(tab => ReferenceEquals(tab.DataContext, module));

    [Test]
    public void EachModule_BringsItsTabs_BoundAgainstItself()
    {
        var surface = new Module { Name = "Surface" };
        var space = new Module { Name = "Space" };
        var ribbon = new Ribbon { TabSetTemplate = ModuleTabs() };

        ribbon.TabSetsSource = new ObservableCollection<Module> { surface, space };

        Assert.Multiple(() =>
        {
            Assert.That(TabOf(ribbon, surface)?.Header, Is.EqualTo("Surface"), "the tab reads its own module");
            Assert.That(TabOf(ribbon, space)?.Header, Is.EqualTo("Space"));
            Assert.That(ribbon.ContextualGroups.Count, Is.EqualTo(2), "a ledge per module");
        });
    }

    [Test]
    public void AModuleThatLeaves_TakesItsTabsWithIt()
    {
        var surface = new Module { Name = "Surface" };
        var space = new Module { Name = "Space" };
        var modules = new ObservableCollection<Module> { surface, space };
        var ribbon = new Ribbon { TabSetTemplate = ModuleTabs(), TabSetsSource = modules };

        modules.Remove(space);

        Assert.Multiple(() =>
        {
            Assert.That(TabOf(ribbon, space), Is.Null);
            Assert.That(TabOf(ribbon, surface), Is.Not.Null, "the one that stayed keeps its tabs");
            Assert.That(ribbon.ContextualGroups.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void HidingAModule_TurnsItsLedgeOff_AndLeavesItInTheDocument()
    {
        var surface = new Module { Name = "Surface" };
        var ribbon = new Ribbon
        {
            TabSetTemplate = ModuleTabs(),
            TabSetsSource = new ObservableCollection<Module> { surface }
        };
        var group = ribbon.ContextualGroups.Single();
        Assert.That(group.IsActive, Is.True, "precondition: the module's tabs are shown");

        surface.IsShown = false;

        Assert.Multiple(() =>
        {
            Assert.That(group.IsActive, Is.False);
            Assert.That(TabOf(ribbon, surface), Is.Not.Null, "hidden, not gone: the tabs come back with the switch");
        });
    }
}
