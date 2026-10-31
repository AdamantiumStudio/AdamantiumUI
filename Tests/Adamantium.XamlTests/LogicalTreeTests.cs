using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>THE LOGICAL PARENT IS WHAT HOLDS AN ELEMENT in a property or a collection - a control its Content and Header,
/// a list its items and its containers, a popup its Child. What shows it - a presenter, a list's panel - only shows it,
/// and the element inherits from where it is shown. A control's template is not in its logical tree.</summary>
[TestFixture]
public class LogicalTreeTests
{
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

    [Test]
    public void AControlsContent_IsTheControls_ThoughItsTemplateShowsIt()
    {
        var text = new TextBlock { Text = "Save" };
        var button = new Button { Content = text };
        Shown(button);

        Assert.Multiple(() =>
        {
            Assert.That(text.LogicalParent, Is.SameAs(button));
            Assert.That(text.ShownIn, Is.InstanceOf<ContentPresenter>());
            Assert.That(text.InheritanceParent, Is.SameAs(text.ShownIn), "it inherits from where it is shown");
        });
    }

    [Test]
    public void ContentATemplateHandsDown_StaysWithTheControlItWasGivenTo()
    {
        var text = new TextBlock { Text = "page" };
        var part = new ContentControl();
        var outer = new ContentControl
        {
            Content = text,
            Template = new ControlTemplate(() =>
            {
                var result = new TemplateResult { RootComponent = part };
                result.AddTemplateBinding(part, "Content", new TemplateBinding { Path = "Content" });
                return result;
            }),
        };
        Shown(outer);

        Assert.Multiple(() =>
        {
            Assert.That(part.Content, Is.SameAs(text), "precondition: the part shows it");
            Assert.That(text.LogicalParent, Is.SameAs(outer), "the part does not take it from the control");
        });
    }

    [Test]
    public void AHeader_IsHeldByItsControl_TillItIsReplaced()
    {
        var header = new TextBlock { Text = "Details" };
        var expander = new Expander { Header = header, Content = new TextBlock { Text = "body" } };
        var window = Shown(expander);
        var held = header.LogicalParent;

        expander.Header = "Other";
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.SameAs(expander));
            Assert.That(header.LogicalParent, Is.Null, "replaced, it is let go");
            Assert.That(header.Text, Is.EqualTo("Details"), "the presenter does not write the new header into the old one");
        });
    }

    [Test]
    public void APopupsChild_IsThePopups()
    {
        var child = new Border();
        var popup = new Popup { Child = child };

        Assert.That(child.LogicalParent, Is.SameAs(popup));
    }

    [Test]
    public void AnElementWrittenIntoAList_IsTheLists_AndItsPanelOnlyShowsIt()
    {
        var item = new Border { Height = 20 };
        var list = new ItemsControl { Height = 200 };
        list.Items.Add(item);
        Shown(list);

        Assert.Multiple(() =>
        {
            Assert.That(item.LogicalParent, Is.SameAs(list));
            Assert.That(item.ShownIn, Is.SameAs(list.ItemsHostPanel), "its panel shows it");
        });
    }

    [Test]
    public void AnElementWrittenIntoAList_WrappedInAContainer_IsTheContainers_AndTheContainerTheLists()
    {
        var item = new Border { Height = 20 };
        var list = new ListBox { Height = 200 };
        list.Items.Add(item);
        Shown(list);

        Assert.Multiple(() =>
        {
            Assert.That(item.LogicalParent, Is.InstanceOf<ListBoxItem>());
            Assert.That(item.LogicalParent?.LogicalParent, Is.SameAs(list));
        });
    }

    [Test]
    public void AMenuNeverOpened_DoesNotTakeItsRows()
    {
        var row = new MenuItem { Header = "Copy" };
        var menu = new ContextMenu();
        menu.Items.Add(row);
        Shown(new Border { ContextMenu = menu });

        Assert.That(row.LogicalParent, Is.Null, "rows of a menu nobody opened are not taken - nor styled");
    }

    [Test]
    public void TheContainersOfAVirtualizingList_AreTheLists_RealizedPooledAndRebound()
    {
        var list = new ListBox { Height = 200, ItemsSource = Enumerable.Range(0, 1000).Select(i => $"Item {i}").ToList() };
        var window = Shown(list);
        var generator = list.ItemContainerGenerator;

        var first = generator.ContainerFromIndex(0);
        ((IScrollableContent)list.ItemsHostPanel).SetOffset(new Vector2<double>(0, 8000));
        Settle(window);

        var realized = generator.RealizedIndices.Select(generator.ContainerFromIndex).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null, "precondition: the top was realized");
            Assert.That(generator.ContainerFromIndex(0), Is.Null, "precondition: scrolled away, the top was recycled");
            Assert.That(realized, Is.Not.Empty);
            Assert.That(realized.Select(c => c.LogicalParent), Is.All.SameAs(list), "realized after scrolling");
            Assert.That(first.LogicalParent, Is.SameAs(list), "recycled, it stays the list's");
        });
    }

    [Test]
    public void TheContainersOfAList_AreLetGo_WhenItsItemsAreReplaced()
    {
        var list = new ListBox { Height = 200, ItemsSource = new[] { "a", "b" } };
        var window = Shown(list);
        var old = list.ItemContainerGenerator.ContainerFromIndex(0);

        list.ItemsSource = new[] { "c" };
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(old, Is.Not.Null, "precondition");
            Assert.That(old.LogicalParent, Is.Null);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0)?.LogicalParent, Is.SameAs(list));
        });
    }

    [Test]
    public void ATabHoldsItsPage_OnlyWhileItIsShown()
    {
        var first = new TextBlock { Text = "first" };
        var second = new TextBlock { Text = "second" };
        var tabs = new TabControl();
        var one = new TabItem { Header = "One", Content = first };
        var two = new TabItem { Header = "Two", Content = second };
        tabs.Items.Add(one);
        tabs.Items.Add(two);
        var window = Shown(tabs);

        var before = (first.LogicalParent, second.LogicalParent);
        tabs.SelectedIndex = 1;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(before.Item1, Is.SameAs(one), "the page shown is its tab's");
            Assert.That(before.Item2, Is.Null, "a page not opened yet is nobody's - its view model waits");
            Assert.That(first.LogicalParent, Is.Null);
            Assert.That(second.LogicalParent, Is.SameAs(two));
        });
    }

    [Test]
    public void ATabControlsPage_IsReachedGoingDownItsLogicalTree()
    {
        var page = new TextBlock { Text = "page" };
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "One", Content = page });
        Shown(tabs);

        Assert.That(IsLogicalDescendant(tabs, page), Is.True, "TabControl > TabItem > page, not through its template");
    }

    private static bool IsLogicalDescendant(IFundamentalUIComponent root, IFundamentalUIComponent element)
    {
        foreach (var child in root.LogicalChildren)
        {
            if (ReferenceEquals(child, element) || IsLogicalDescendant(child, element)) return true;
        }

        return false;
    }

    [Test]
    public void AnElementShownByOneControl_TakenByAnother_InheritsFromItsNewPlace()
    {
        var view = new TextBlock { Text = "view" };
        var first = new Button { DataContext = "first", Content = view };
        var border = new Border { DataContext = "second" };
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(border);
        var window = Shown(panel);

        first.Content = null;
        border.Child = view;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(view.LogicalParent, Is.SameAs(border));
            Assert.That(view.ShownIn, Is.Null);
            Assert.That(view.DataContext, Is.EqualTo("second"));
        });
    }

    [Test]
    public void ContentLetGoByItsHolder_GoesToTheControlBoundToIt()
    {
        var view = new TextBlock { Text = "view" };
        var holder = new ContentControl { Visibility = Visibility.Collapsed, Content = view };
        var other = new ContentControl { DataContext = new ViewSource(view) };
        var panel = new StackPanel();
        panel.Children.Add(holder);
        panel.Children.Add(other);
        var window = Shown(panel);
        other.SetBinding(nameof(ContentControl.Content), new Binding(nameof(ViewSource.View)));
        Settle(window);
        var heldFirst = view.LogicalParent;

        holder.Content = null;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(heldFirst, Is.SameAs(holder), "bound to a view another holds, the control does not take it");
            Assert.That(view.LogicalParent, Is.SameAs(other), "let go, it goes to the control whose Content it is");
        });
    }

    [Test]
    public void ContentGivenToAnotherControl_IsTheOnlyChildOfTheNewOne()
    {
        var view = new TextBlock { Text = "view" };
        var a = new Button { Content = view };
        var b = new ContentControl();
        var panel = new StackPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        var window = Shown(panel);

        b.Content = view;
        Settle(window);

        var listedUnder = Enumerable.Count(new IFundamentalUIComponent[] { a, b }.Concat(a.GetVisualDescendants()),
            node => node.LogicalChildren.Contains(view));
        Assert.Multiple(() =>
        {
            Assert.That(view.LogicalParent, Is.SameAs(b));
            Assert.That(listedUnder, Is.EqualTo(1), "one logical parent lists it");
        });
    }

    [Test]
    public void AnElementMovedBetweenPresenters_IsLetGoByTheFirst()
    {
        var view = new TextBlock { Text = "view" };
        var one = new ContentPresenter { DataContext = "one" };
        var two = new ContentPresenter { DataContext = "two" };
        var panel = new StackPanel();
        panel.Children.Add(one);
        panel.Children.Add(two);
        var window = Shown(panel);

        one.Content = view;
        Settle(window);
        two.Content = view;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(view.LogicalParent, Is.SameAs(two));
            Assert.That(view.DataContext, Is.EqualTo("two"));
        });
    }

    [Test]
    public void ADiscardedControl_LetsItsContentGo()
    {
        var view = new TextBlock { Text = "view" };
        var host = new ContentControl { Content = view };
        Shown(host);

        Adamantium.UI.Core.DiscardedVisuals.ReleaseNow(new IUIComponent[] { host });

        Assert.Multiple(() =>
        {
            Assert.That(view.LogicalParent, Is.Null);
            Assert.That(view.ShownIn, Is.Null);
            Assert.That(((UIComponent)view).ContentOwner, Is.Null, "a view a view model keeps does not keep the control");
        });
    }

    [Test]
    public void AControlsContent_IsShownByTheControl_ItsTemplateTakenAway()
    {
        var view = new TextBlock { Text = "view" };
        var host = new ContentControl { Content = view };
        var window = Shown(host);

        host.Template = null;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(view.VisualParent, Is.SameAs(host));
            Assert.That(view.LogicalParent, Is.SameAs(host));
        });
    }
}
