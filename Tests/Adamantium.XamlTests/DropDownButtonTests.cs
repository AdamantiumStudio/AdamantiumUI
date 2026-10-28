using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Drop-down and split buttons in the theme's own template: the list drops, the keyboard walks it, a pick runs
/// the item's command and the button's, and a split button's body runs its own.</summary>
[TestFixture]
public class DropDownButtonTests
{
    private FakeApp _app;
    private Window _window;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void UseFluent()
    {
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    [TearDown]
    public void LetGo() => FocusManager.ResetFocus();

    private T Shown<T>(T button) where T : DropDownButton
    {
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.VerticalAlignment = VerticalAlignment.Top;
        _window = new Window { Width = 800, Height = 600, Content = button };
        _window.ApplyCurrentTheme();
        Settle();
        FocusManager.Focus(button, NavigationMethod.Tab);
        Settle();
        return button;
    }

    private void Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
            _window.PopupLayer.UpdateLayout(new Adamantium.Mathematics.Size<double>(_window.Width, _window.Height));
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static bool Press(IInputComponent target, Key key, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
        if (args.Handled) return true;

        args.RoutedEvent = Keyboard.KeyDownEvent;
        target.RaiseEvent(args);
        return args.Handled;
    }

    private static DropDownButtonItem Row(DropDownButton button, int index) =>
        (DropDownButtonItem)button.ItemContainerGenerator.ContainerFromIndex(index);

    [Test]
    public void APick_RunsTheItemCommand_AndClosesTheList()
    {
        var ran = new List<object>();
        var clicked = new List<object>();
        var button = Shown(new DropDownButton
        {
            Content = "Export", ItemsSource = new[] { "PDF", "PNG", "SVG" },
            ItemCommand = new AdamantiumCommand<object>(ran.Add),
        });
        button.ItemClick += (_, e) => clicked.Add(e.Item);

        Assert.That(Press(button, Key.Enter), Is.True);
        Settle();
        Assert.That(button.IsDropDownOpen, Is.True, "Enter opens the list");

        Row(button, 1).PerformClick();
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(new object[] { "PNG" }));
            Assert.That(clicked, Is.EqualTo(new object[] { "PNG" }));
            Assert.That(button.IsDropDownOpen, Is.False);
        });
    }

    [Test]
    public void TheArrows_WalkTheList_AndEnterPicksWhereTheyStand()
    {
        var ran = new List<object>();
        var button = Shown(new DropDownButton
        {
            Content = "Export", ItemsSource = new[] { "PDF", "PNG", "SVG" },
            ItemCommand = new AdamantiumCommand<object>(ran.Add),
        });

        Press(button, Key.F4);
        Settle();
        Press(button, Key.DownArrow);
        Press(button, Key.DownArrow);
        Assert.That(Row(button, 1).IsHighlighted, Is.True);
        Press(button, Key.Enter);
        Settle();

        Assert.That(ran, Is.EqualTo(new object[] { "PNG" }));
        Press(button, Key.DownArrow, InputModifiers.LeftAlt);
        Settle();
        Assert.That(button.IsDropDownOpen, Is.True, "Alt+Down opens it");
        Press(button, Key.Escape);
        Assert.That(button.IsDropDownOpen, Is.False, "Escape closes it");
    }

    [Test]
    public void ARowWrittenInPlace_RunsItsOwnCommandToo()
    {
        var own = 0;
        var picked = new List<object>();
        var row = new DropDownButtonItem { Content = "Save", Command = new AdamantiumCommand(() => own++) };
        var button = new DropDownButton { Content = "File", ItemCommand = new AdamantiumCommand<object>(picked.Add) };
        button.Items.Add(row);
        Shown(button);
        button.IsDropDownOpen = true;
        Settle();

        row.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(own, Is.EqualTo(1));
            Assert.That(picked, Is.EqualTo(new object[] { row }));
        });
    }

    [Test]
    public void ASplitButtonsBody_RunsItsCommand_AndItsArrowDropsTheList()
    {
        var body = 0;
        var button = Shown(new SplitButton
        {
            Content = "Paste", Command = new AdamantiumCommand(() => body++), ItemsSource = new[] { "Keep", "Text" },
        });

        Press(button, Key.Enter);
        Assert.Multiple(() =>
        {
            Assert.That(body, Is.EqualTo(1), "Enter presses the body");
            Assert.That(button.IsDropDownOpen, Is.False);
            Assert.That(button.GetTemplateChild("PART_DropDownArea"), Is.Not.Null);
        });

        Press(button, Key.DownArrow, InputModifiers.LeftAlt);
        Settle();
        Assert.That(button.IsDropDownOpen, Is.True);
    }

    [Test]
    public void AToggleSplitButtonsBody_StaysDown()
    {
        var turned = 0;
        var button = Shown(new ToggleSplitButton { Content = "Bullets", ItemsSource = new[] { "Discs" } });
        button.IsCheckedChanged += (_, _) => turned++;
        var peer = (ToggleSplitButtonAutomationPeer)button.GetAutomationPeer();

        button.PerformClick();
        Assert.Multiple(() =>
        {
            Assert.That(button.IsChecked, Is.True);
            Assert.That(turned, Is.EqualTo(1));
            Assert.That(peer.ToggleState, Is.EqualTo(ToggleState.On));
        });

        peer.Toggle();
        Assert.That(button.IsChecked, Is.False);
    }

    public sealed class Saving
    {
        public int Saved;

        public AdamantiumCommand SaveCommand => new(() => Saved++);
    }

    [Test]
    public void ARowWrittenInPlace_BindsToTheButtonsData_NotToTheButton()
    {
        var data = new Saving();
        var row = new DropDownButtonItem { Content = "Save" };
        row.SetBinding(Adamantium.UI.Controls.Primitives.ButtonBase.CommandProperty,
            new Adamantium.UI.Core.Data.Binding(nameof(Saving.SaveCommand)));
        var button = new DropDownButton { Content = "File", DataContext = data };
        button.Items.Add(row);
        Shown(button);
        button.IsDropDownOpen = true;
        Settle();

        row.PerformClick();

        Assert.That(data.Saved, Is.EqualTo(1));
    }

    [Test]
    public void ABodyThatCannotRun_LeavesTheListReachable()
    {
        var button = Shown(new SplitButton
        {
            Content = "Paste", Command = new AdamantiumCommand(() => { }, () => false), ItemsSource = new[] { "Text" },
        });

        Assert.Multiple(() =>
        {
            Assert.That(button.IsEnabled, Is.True);
            Assert.That(button.IsBodyEnabled, Is.False);
        });
        Press(button, Key.DownArrow, InputModifiers.LeftAlt);
        Settle();
        Assert.That(button.IsDropDownOpen, Is.True);
    }

    [Test]
    public void TheArrows_SkipRowsThatCannotRun_AndForgetARowThatLeft()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "A", "B", "C" };
        var ran = new List<object>();
        var button = Shown(new DropDownButton
        {
            Content = "Go", ItemsSource = items, ItemCommand = new AdamantiumCommand<object>(ran.Add, item => (string)item != "B"),
        });
        Press(button, Key.F4);
        Settle();

        Assert.That(Row(button, 1).IsAvailable, Is.False, "B cannot run");
        Assert.That(Row(button, 1).IsEnabled, Is.True, "and its own enabled state is left alone");
        Press(button, Key.DownArrow);
        Press(button, Key.DownArrow);
        Assert.That(Row(button, 2).IsHighlighted, Is.True, "the walk steps over B");

        items.RemoveAt(2);
        items.RemoveAt(1);
        Settle();
        Press(button, Key.UpArrow);
        Assert.That(Row(button, 0).IsHighlighted, Is.True);
        Press(button, Key.Enter);
        Assert.That(ran, Is.EqualTo(new object[] { "A" }));
    }

    [Test]
    public void EnterOnAListWithNothingLit_ClosesIt()
    {
        var button = Shown(new DropDownButton { Content = "Go", ItemsSource = new[] { "A" } });
        Press(button, Key.F4);
        Settle();

        Press(button, Key.Enter);

        Assert.That(button.IsDropDownOpen, Is.False);
    }

    private sealed class Answered : ToggleSplitButton
    {
        public void Answer(bool on) => OnCommandState(new CommandState(true, on));
    }

    [Test]
    public void AToggleSplitButton_ShowsWhatItsCommandAnswers()
    {
        var button = Shown(new Answered { Content = "Bullets" });

        button.Answer(true);

        Assert.That(button.IsChecked, Is.True);
    }

    [Test]
    public void AClosedList_ShowsAutomationNoRows()
    {
        var button = Shown(new DropDownButton { Content = "Go", ItemsSource = new[] { "A", "B" } });
        var peer = (DropDownButtonAutomationPeer)button.GetAutomationPeer();

        Assert.That(peer.GetChildren(), Is.Empty);
        peer.Expand();
        Settle();
        Assert.That(peer.GetChildren(), Has.Count.EqualTo(2));
    }

    public sealed class Opening : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _open;

        public bool Open
        {
            get => _open;
            set
            {
                _open = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Open)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    [Test]
    public void TheKeyboard_LeavesABindingOfTheOpenStateInPlace()
    {
        var data = new Opening();
        var button = new DropDownButton { Content = "Go", ItemsSource = new[] { "A" }, DataContext = data };
        button.SetBinding(DropDownButton.IsDropDownOpenProperty,
            new Adamantium.UI.Core.Data.Binding(nameof(Opening.Open)) { Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay });
        Shown(button);

        Press(button, Key.F4);
        Press(button, Key.Escape);
        Settle();
        data.Open = true;
        Settle();

        Assert.That(button.IsDropDownOpen, Is.True, "the view-model still opens it");
    }

    [Test]
    public void ADisabledButton_ClosesItsList_AndABodyThatCannotRunRefusesAutomation()
    {
        var split = Shown(new SplitButton
        {
            Content = new Adamantium.UI.Controls.Text.TextBlock { Text = "Paste" },
            Command = new AdamantiumCommand(() => { }, () => false), ItemsSource = new[] { "Keep" },
        });
        var peer = (SplitButtonAutomationPeer)split.GetAutomationPeer();

        Assert.Multiple(() =>
        {
            Assert.Throws<System.InvalidOperationException>(() => peer.Invoke());
            Assert.That(peer.Name, Is.EqualTo("Paste"), "named by the text it draws");
        });

        peer.Expand();
        Settle();
        split.IsEnabled = false;
        Assert.That(split.IsDropDownOpen, Is.False);
    }

    [Test]
    public void ARowTakenOutOfTheList_LetsGoOfItsHighlight()
    {
        var first = new DropDownButtonItem { Content = "A" };
        var second = new DropDownButtonItem { Content = "B" };
        var button = new DropDownButton { Content = "Go" };
        button.Items.Add(first);
        button.Items.Add(second);
        Shown(button);
        Press(button, Key.F4);
        Settle();
        Press(button, Key.DownArrow);
        Press(button, Key.DownArrow);
        Assert.That(second.IsHighlighted, Is.True);

        button.Items.Remove(second);
        Settle();

        Assert.That(second.IsHighlighted, Is.False);
    }

    [Test]
    public void ARowTheAuthorDisabled_StaysDisabled_AndOneLitRowAtATime()
    {
        var off = new DropDownButtonItem { Content = "Off", IsEnabled = false };
        var items = new System.Collections.ObjectModel.ObservableCollection<object> { "A", off, "B" };
        var button = Shown(new DropDownButton { Content = "Go", ItemsSource = items });
        Press(button, Key.F4);
        Settle();

        Assert.That(off.IsEnabled, Is.False, "opening the list leaves the author's word");
        Press(button, Key.DownArrow);
        Press(button, Key.DownArrow);
        Assert.That(Row(button, 2).IsHighlighted, Is.True, "the walk steps over it");

        items.Insert(0, "Z");
        Settle();
        Press(button, Key.DownArrow);
        var lit = Enumerable.Range(0, items.Count).Count(i => button.ItemContainerGenerator.ContainerFromIndex(i) is DropDownButtonItem { IsHighlighted: true });
        Assert.That(lit, Is.EqualTo(1));
    }

    [Test]
    public void ACommandThatTakesItsItemAway_StillReportsTheItem()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "A", "B" };
        var clicked = new List<object>();
        var button = Shown(new DropDownButton
        {
            Content = "Go", ItemsSource = items, ItemCommand = new AdamantiumCommand<object>(item => items.Remove((string)item)),
        });
        button.ItemClick += (_, e) => clicked.Add(e.Item);
        button.IsDropDownOpen = true;
        Settle();

        Row(button, 1).PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(new[] { "A" }));
            Assert.That(clicked, Is.EqualTo(new object[] { "B" }));
        });
    }

    [Test]
    public void ADisabledButton_DoesNotOpen_AndTellsItsViewModelSo()
    {
        var data = new Opening();
        var button = new DropDownButton { Content = "Go", ItemsSource = new[] { "A" }, IsEnabled = false, DataContext = data };
        button.SetBinding(DropDownButton.IsDropDownOpenProperty,
            new Adamantium.UI.Core.Data.Binding(nameof(Opening.Open)) { Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay });
        Shown(button);

        data.Open = true;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(button.IsDropDownOpen, Is.False);
            Assert.That(data.Open, Is.False);
        });
    }

    [Test]
    public void ARowMovedToAnotherButton_IsPickedThere()
    {
        var picked = new List<string>();
        var row = new DropDownButtonItem { Content = "Row" };
        var first = new DropDownButton { Content = "A" };
        first.ItemClick += (_, _) => picked.Add("A");
        var second = new DropDownButton { Content = "B" };
        second.ItemClick += (_, _) => picked.Add("B");
        first.Items.Add(row);
        var both = new Adamantium.UI.Controls.Panels.StackPanel();
        both.Children.Add(first);
        both.Children.Add(second);
        _window = new Window { Width = 800, Height = 600, Content = both };
        _window.ApplyCurrentTheme();
        Settle();
        first.IsDropDownOpen = true;
        Settle();
        first.IsDropDownOpen = false;
        Settle();

        first.Items.Clear();
        second.Items.Add(row);
        row.PerformClick();

        Assert.That(picked, Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public void AViewModelThatOpensADisabledButtonFromTheStart_HearsItWasRefused()
    {
        var data = new Opening { Open = true };
        var button = new DropDownButton { Content = "Go", ItemsSource = new[] { "A" }, IsEnabled = false, DataContext = data };
        button.SetBinding(DropDownButton.IsDropDownOpenProperty,
            new Adamantium.UI.Core.Data.Binding(nameof(Opening.Open)) { Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay });
        Shown(button);
        LoopSignal.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(button.IsDropDownOpen, Is.False);
            Assert.That(data.Open, Is.False);
        });
    }

    [Test]
    public void ARowsOwnCommandArrivingLate_MakesItAvailable()
    {
        var saved = 0;
        var row = new DropDownButtonItem { Content = "Save" };
        var button = new DropDownButton { Content = "File", ItemCommand = new AdamantiumCommand<object>(_ => { }, _ => false) };
        button.Items.Add(row);
        Shown(button);
        button.IsDropDownOpen = true;
        Settle();
        Assert.That(row.IsAvailable, Is.False);

        row.Command = new AdamantiumCommand(() => saved++);
        row.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(row.IsAvailable, Is.True);
            Assert.That(saved, Is.EqualTo(1));
        });
    }

    [Test]
    public void ARowIsNamedByWhatItShows()
    {
        var button = Shown(new DropDownButton
        {
            Content = "Export", ItemsSource = new[] { "Pdf" },
            ItemTemplate = new Adamantium.UI.Core.Templates.DataTemplate(() => new Adamantium.UI.Core.Templates.TemplateResult
            {
                RootComponent = new Adamantium.UI.Controls.Text.TextBlock { Text = "As PDF" },
            }),
        });
        button.IsDropDownOpen = true;
        Settle();

        Assert.That(Row(button, 0).GetAutomationPeer().Name, Is.EqualTo("As PDF"));
    }

    [Test]
    public void Automation_ExpandsTheList_AndInvokesTheBody()
    {
        var body = 0;
        var split = Shown(new SplitButton { Content = "Paste", Command = new AdamantiumCommand(() => body++), ItemsSource = new[] { "Keep" } });
        var peer = (SplitButtonAutomationPeer)split.GetAutomationPeer();

        peer.Expand();
        Settle();
        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.SplitButton));
            Assert.That(peer.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Expanded));
            Assert.That(peer.Name, Is.EqualTo("Paste"));
            Assert.That(peer.GetChildren().Single().ControlType, Is.EqualTo(AutomationControlType.MenuItem));
        });

        peer.Collapse();
        peer.Invoke();
        Assert.That(body, Is.EqualTo(1));
    }
}
