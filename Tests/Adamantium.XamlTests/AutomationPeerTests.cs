using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Adamantium.Core.Commands;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Controls describe themselves to automation through peers: what they are, what they are called, which id finds them,
/// and what can be done with them - by the same path a click or a keystroke takes. Built under a theme, so a button's
/// template is there to be looked past.
/// </summary>
[TestFixture]
public class AutomationPeerTests
{
    private FakeApp _app;

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

    [Test]
    public void TheTree_LooksThroughPanels_AndNotIntoAButtonsTemplate()
    {
        var save = new Button { Content = "Save" };
        var hello = new TextBlock { Text = "Hello" };
        var wrap = new CheckBox { Content = "Wrap" };
        var stack = new StackPanel();
        stack.Children.Add(save);
        stack.Children.Add(hello);
        stack.Children.Add(wrap);
        var host = new ContentControl { Content = new Border { Child = stack } };
        Shown(host);

        var children = host.GetAutomationPeer().GetChildren();
        AutomationControlType[] types = [AutomationControlType.Button, AutomationControlType.Text, AutomationControlType.CheckBox];
        string[] names = ["Save", "Hello", "Wrap"];

        Assert.Multiple(() =>
        {
            Assert.That(children.Select(peer => peer.ControlType), Is.EqualTo(types));
            Assert.That(children.Select(peer => peer.Name), Is.EqualTo(names));
            Assert.That(save.GetAutomationPeer().GetChildren(), Is.Empty, "the label of its template is its name, not a child");
            Assert.That(save.GetAutomationPeer().GetParent(), Is.SameAs(host.GetAutomationPeer()),
                "the panel and the border have no peer of their own");
        });
    }

    [Test]
    public void TheId_IsTheElementsName_UnlessOneIsGiven()
    {
        var named = new Button { Name = "SaveButton", Content = "Save" };
        var given = new Button { Name = "Other", Content = "Open" };
        AutomationProperties.SetAutomationId(given, "Open");

        Assert.Multiple(() =>
        {
            Assert.That(named.GetAutomationPeer().AutomationId, Is.EqualTo("SaveButton"));
            Assert.That(given.GetAutomationPeer().AutomationId, Is.EqualTo("Open"));
        });
    }

    [Test]
    public void AGivenName_OverridesTheContent()
    {
        var button = new Button { Content = "X" };
        AutomationProperties.SetName(button, "Close");

        Assert.That(button.GetAutomationPeer().Name, Is.EqualTo("Close"));
    }

    [Test]
    public void Invoke_DoesWhatAClickDoes()
    {
        var command = new Counting();
        var clicked = 0;
        var button = new Button { Content = "Run", Command = command };
        button.Click += (_, _) => clicked++;
        Shown(button);

        ((IInvokeProvider)button.GetAutomationPeer().GetPattern(PatternId.Invoke)).Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(command.Runs, Is.EqualTo(1), "the command ran");
            Assert.That(clicked, Is.EqualTo(1), "and Click was raised");
        });
    }

    [Test]
    public void ARibbonRadioButton_IsAChoiceSelectedAsAPressSelectsIt_AndCannotBeToggledOff()
    {
        var select = new RibbonRadioButton { Content = "Select", GroupName = "Tool", IsChecked = true };
        var move = new RibbonRadioButton { Content = "Move", GroupName = "Tool" };
        var group = new RibbonGroup { Header = "Tools" };
        group.Items.Add(select);
        group.Items.Add(move);
        Shown(group);
        var peer = move.GetAutomationPeer();

        ((ISelectionItemProvider)peer.GetPattern(PatternId.SelectionItem)).Select();

        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.RadioButton));
            Assert.That(peer.Name, Is.EqualTo("Move"));
            Assert.That(move.IsChecked, Is.True);
            Assert.That(select.IsChecked, Is.False, "the tool chosen before stayed checked");
            Assert.That(((ISelectionItemProvider)select.GetAutomationPeer()).IsSelected, Is.False);
            Assert.That(peer.GetPattern(PatternId.Toggle), Is.Null, "a choice is never toggled off");
        });
    }

    [Test]
    public void AToggleButton_Toggles_AndIsNotInvoked()
    {
        var box = new CheckBox { Content = "Wrap" };
        Shown(box);
        var peer = box.GetAutomationPeer();

        ((IToggleProvider)peer.GetPattern(PatternId.Toggle)).Toggle();

        Assert.Multiple(() =>
        {
            Assert.That(box.IsChecked, Is.True);
            Assert.That(((IToggleProvider)peer).ToggleState, Is.EqualTo(ToggleState.On));
            Assert.That(peer.GetPattern(PatternId.Invoke), Is.Null);
        });
    }

    [Test]
    public void SetValue_WritesTheText_AndTheBindingKeepsFollowing()
    {
        var model = new Model { Text = "before" };
        var box = new TextBox { DataContext = model };
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(Model.Text)));
        Shown(box);
        var value = (IValueProvider)box.GetAutomationPeer().GetPattern(PatternId.Value);

        value.SetValue("typed");
        BindingUpdateQueue.Flush();
        Assert.That(model.Text, Is.EqualTo("typed"), "written back to the source");

        model.Text = "changed";
        BindingUpdateQueue.Flush();
        Assert.That(value.Value, Is.EqualTo("changed"), "and the binding was not cut");
    }

    [Test]
    public void Select_SelectsTheTab()
    {
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "One" });
        tabs.Items.Add(new TabItem { Header = "Two" });
        Shown(tabs);
        var peer = tabs.GetAutomationPeer();
        var two = peer.GetChildren().Single(child => child.Name == "Two");

        ((ISelectionItemProvider)two.GetPattern(PatternId.SelectionItem)).Select();
        AutomationPeer[] selected = [two];

        Assert.Multiple(() =>
        {
            Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
            Assert.That(((ISelectionProvider)peer.GetPattern(PatternId.Selection)).GetSelection(), Is.EqualTo(selected));
            Assert.That(((ISelectionItemProvider)two).SelectionContainer, Is.SameAs(peer));
        });
    }

    [Test]
    public void ACaptionCommand_IsFoundByItsId_AndCalledByItsLabel()
    {
        var window = new Window
        {
            Width = 800,
            Height = 600,
            ClientWidth = 800,
            ClientHeight = 600,
            RightWindowCommands = new List<WindowCommand> { new() { AutomationId = "OpenRibbon", Label = "Ribbon" } }
        };
        for (var i = 0; i < 3; i++)
        {
            window.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            BindingUpdateQueue.Flush();
        }

        var command = Descendants(window.GetAutomationPeer()).Single(peer => peer.AutomationId == "OpenRibbon");

        Assert.Multiple(() =>
        {
            Assert.That(command.ControlType, Is.EqualTo(AutomationControlType.Button));
            Assert.That(command.Name, Is.EqualTo("Ribbon"));
        });
    }

    [Test]
    public void TabsMadeFromData_TakeTheirIdsFromTheItemContainerStyle()
    {
        var style = new Style { Selector = new StyleSelector { Types = { typeof(TabItem) } } };
        style.Setters.Add(new Setter("AutomationProperties.AutomationId", new Binding(nameof(Page.Key))));
        var tabs = new TabControl { ItemContainerStyle = style };
        tabs.ItemsSource = new List<Page> { new() { Key = "Buttons" }, new() { Key = "Docking" } };
        Shown(tabs);

        var ids = tabs.GetAutomationPeer().GetChildren().OfType<TabItemAutomationPeer>().Select(tab => tab.AutomationId);
        string[] expected = ["Buttons", "Docking"];

        Assert.That(ids, Is.EqualTo(expected));
    }

    [Test]
    public void ACollapsedElement_IsOffscreen()
    {
        var shown = new Button { Content = "Shown" };
        var collapsed = new Button { Content = "Gone", Visibility = Visibility.Collapsed };
        var stack = new StackPanel();
        stack.Children.Add(shown);
        stack.Children.Add(collapsed);
        Shown(stack);

        Assert.Multiple(() =>
        {
            Assert.That(shown.GetAutomationPeer().IsOffscreen, Is.False);
            Assert.That(collapsed.GetAutomationPeer().IsOffscreen, Is.True);
        });
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer) =>
        peer.GetChildren().SelectMany(child => Descendants(child).Prepend(child));

    private sealed class Page
    {
        public string Key { get; init; }
    }

    private sealed class Counting : ICommand
    {
        public int Runs { get; private set; }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter = null) => true;

        public void Execute(object parameter = null) => Runs++;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Model : INotifyPropertyChanged
    {
        private string _text;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }
    }
}
