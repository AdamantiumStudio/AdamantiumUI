using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Numbers between limits, scrolling, folding and windows to automation: a range control is set and read as a number, a
/// range slider's handles are its children, a scroll viewer and a list scroll to percents, an expander opens and folds,
/// a window is minimized and restored, and an element is named by the label it points at.
/// </summary>
[TestFixture]
public class AutomationRangeTests
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

    private static async Task<(AutomationSession Session, Window Window)> Driving(UIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return (session, window);
    }

    [Test]
    public async Task ASlider_IsSetAsANumber_InsideItsLimits()
    {
        var slider = new Slider { Name = "Volume", Minimum = 0, Maximum = 50, Value = 10 };
        var (session, _) = await Driving(slider);
        await using var _ = session;

        await session.Find(By.Id("Volume")).SetValueAsync("32.5");
        var info = await session.Find(By.Id("Volume")).GetAsync();
        var outside = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Volume")).SetValueAsync("51"));

        Assert.Multiple(() =>
        {
            Assert.That(slider.Value, Is.EqualTo(32.5));
            Assert.That(info.ControlType, Is.EqualTo("Slider"));
            Assert.That(info.Value, Is.EqualTo("32.5"));
            Assert.That(info.Minimum, Is.EqualTo(0));
            Assert.That(info.Maximum, Is.EqualTo(50));
            Assert.That(outside.Message, Does.Contain("Outside"));
            Assert.That(slider.Value, Is.EqualTo(32.5));
        });
    }

    [Test]
    public async Task ASetValue_KeepsTheSlidersBindingAlive()
    {
        var source = new Holder { Level = 5 };
        var slider = new Slider { Name = "Level", Minimum = 0, Maximum = 100, DataContext = source };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(Holder.Level)) { Mode = BindingMode.TwoWay });
        var (session, _) = await Driving(slider);
        await using var _ = session;

        await session.Find(By.Id("Level")).SetValueAsync("40");
        source.Level = 70;
        await session.WaitForIdleAsync();

        Assert.That(slider.Value, Is.EqualTo(70), "the value automation wrote must not cut the binding off");
    }

    [Test]
    public async Task AProgressBar_IsReadNotSet()
    {
        var bar = new ProgressBar { Name = "Upload", Maximum = 200, Value = 50 };
        var (session, _) = await Driving(bar);
        await using var _ = session;

        var info = await session.Find(By.Id("Upload")).GetAsync();
        var refusal = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Upload")).SetValueAsync("60"));

        Assert.Multiple(() =>
        {
            Assert.That(info.ControlType, Is.EqualTo("ProgressBar"));
            Assert.That(info.Value, Is.EqualTo("50"));
            Assert.That(info.Maximum, Is.EqualTo(200));
            Assert.That(refusal.Message, Does.Contain("read-only"));
            Assert.That(bar.Value, Is.EqualTo(50));
        });
    }

    [Test]
    public async Task ARangeSlidersHandles_AreItsChildren_EachHoldingOneBound()
    {
        var span = new RangeSlider { Name = "Span", Minimum = 0, Maximum = 100, LowerValue = 20, UpperValue = 70, Width = 300 };
        var (session, _) = await Driving(span);
        await using var _ = session;

        var handles = await session.FindAllAsync(By.Type(AutomationControlType.Thumb));
        await session.Find(By.Id("Span")).Find(By.Id("Upper")).SetValueAsync("90");
        await session.Find(By.Id("Span")).Find(By.Id("Lower")).SetValueAsync("35");
        var lower = await session.Find(By.Id("Span")).Find(By.Id("Lower")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handles.Select(handle => handle.AutomationId), Is.EqualTo(new[] { "Lower", "Upper" }));
            Assert.That(span.UpperValue, Is.EqualTo(90));
            Assert.That(span.LowerValue, Is.EqualTo(35));
            Assert.That(lower.Value, Is.EqualTo("35"));
            Assert.That(lower.Path, Does.Contain("#Span"));
        });
    }

    [Test]
    public async Task ANumericUpDown_HoldsItsNumberBetweenLimits()
    {
        var numeric = new NumericUpDown { Name = "Count", Minimum = -5, Maximum = 5, Value = 1 };
        var (session, _) = await Driving(numeric);
        await using var _ = session;

        await session.Find(By.Id("Count")).SetValueAsync("-3");
        var info = await session.Find(By.Id("Count")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(numeric.Value, Is.EqualTo(-3));
            Assert.That(info.Patterns, Does.Contain("RangeValue").And.Contain("Value"));
            Assert.That(info.Minimum, Is.EqualTo(-5));
            Assert.That(info.Maximum, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task AScrollViewer_ScrollsToPercents_AxisByAxis()
    {
        var viewer = new ScrollViewer
        {
            Name = "Map",
            Width = 400,
            Height = 300,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 2000, Height = 1500 }
        };
        var (session, _) = await Driving(viewer);
        await using var _ = session;

        await session.Find(By.Id("Map")).ScrollToAsync(null, 100);
        var bottom = await session.Find(By.Id("Map")).GetAsync();
        var offsetAtBottom = viewer.ScrollOffset;
        await session.Find(By.Id("Map")).ScrollToAsync(50, null);
        var middle = await session.Find(By.Id("Map")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(bottom.VerticalScroll, Is.EqualTo(100));
            Assert.That(bottom.HorizontalScroll, Is.EqualTo(0));
            Assert.That(offsetAtBottom.Y, Is.EqualTo(viewer.ExtentSize.Height - viewer.ViewportSize.Height).Within(0.5));
            Assert.That(middle.VerticalScroll, Is.EqualTo(100), "an axis left out stays where it is");
            Assert.That(middle.HorizontalScroll, Is.EqualTo(50));
        });
    }

    [Test]
    public async Task AList_ScrollsThroughItsOwnViewer_AndItsLastRowGetsAnElement()
    {
        var rows = Enumerable.Range(0, 500).Select(index => $"Row {index}").ToList();
        var list = new ListBox { Name = "Rows", Height = 200, ItemsSource = rows };
        var (session, _) = await Driving(list);
        await using var _ = session;

        await session.Find(By.Id("Rows")).ScrollToAsync(null, 100);
        var info = await session.Find(By.Id("Rows")).GetAsync();
        var last = await session.Find(By.Id("Rows")).Find(By.Name("Row 499")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(info.Patterns, Does.Contain("Scroll"));
            Assert.That(info.VerticalScroll, Is.EqualTo(100));
            Assert.That(last.ClassName, Is.EqualTo(nameof(ListBoxItem)), "the last row was made when scrolled to");
            Assert.That(last.IsOffscreen, Is.False);
        });
    }

    [Test]
    public async Task AnExpander_OpensAndFolds()
    {
        var expander = new Expander { Name = "Details", Header = "Details", Content = new TextBlock { Text = "Inside" } };
        var (session, _) = await Driving(expander);
        await using var _ = session;

        await session.Find(By.Id("Details")).ExpandAsync();
        var opened = expander.IsExpanded;
        await session.Find(By.Id("Details")).CollapseAsync();
        var info = await session.Find(By.Id("Details")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.True);
            Assert.That(expander.IsExpanded, Is.False);
            Assert.That(info.ControlType, Is.EqualTo("Group"));
            Assert.That(info.Name, Is.EqualTo("Details"));
            Assert.That(info.ExpandCollapseState, Is.EqualTo("Collapsed"));
        });
    }

    [Test]
    public async Task AWindow_IsMinimizedAndRestored()
    {
        var (session, window) = await Driving(new Border());
        await using var _ = session;

        await session.Find(By.Type(AutomationControlType.Window)).SetWindowStateAsync(WindowState.Minimized);
        var minimized = await session.Find(By.Type(AutomationControlType.Window)).GetAsync();
        await session.Find(By.Type(AutomationControlType.Window)).SetWindowStateAsync(WindowState.Normal);

        Assert.Multiple(() =>
        {
            Assert.That(minimized.WindowState, Is.EqualTo("Minimized"));
            Assert.That(minimized.Patterns, Does.Contain("Window"));
            Assert.That(window.State, Is.EqualTo(WindowState.Normal));
        });
    }

    [Test]
    public async Task AnElement_IsNamedByTheLabelItPointsAt_AndFollowsIt()
    {
        var label = new TextBlock { Name = "VolumeLabel", Text = "Volume" };
        var slider = new Slider { Name = "Volume" };
        AutomationProperties.SetLabeledBy(slider, label);
        var panel = new StackPanel();
        panel.Children.Add(label);
        panel.Children.Add(slider);
        var (session, _) = await Driving(panel);
        await using var _ = session;

        var named = await session.Find(By.Id("Volume")).NameAsync();
        label.Text = "Loudness";
        var renamed = await session.Find(By.Id("Volume")).NameAsync();
        AutomationProperties.SetName(slider, "Gain");
        var overridden = await session.Find(By.Id("Volume")).NameAsync();

        Assert.Multiple(() =>
        {
            Assert.That(named, Is.EqualTo("Volume"));
            Assert.That(renamed, Is.EqualTo("Loudness"));
            Assert.That(overridden, Is.EqualTo("Gain"), "a name set on the element wins over its label");
        });
    }

    private sealed class Holder : System.ComponentModel.INotifyPropertyChanged
    {
        private double _level;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public double Level
        {
            get => _level;
            set
            {
                _level = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Level)));
            }
        }
    }
}
