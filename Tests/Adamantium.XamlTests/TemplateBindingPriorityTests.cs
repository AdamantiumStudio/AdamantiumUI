using System.ComponentModel;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What a template's markup binds stands where its values do: the template's triggers outrank it, a control
/// writing its own current value leaves it bound, and a binding's fallback can be a resource the template's tree holds.
/// </summary>
[TestFixture]
public class TemplateBindingPriorityTests
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

    private sealed class Item : INotifyPropertyChanged
    {
        private string name;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get => name;
            set
            {
                name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }

    private static Window Shown(UIComponent content)
    {
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = content };
        Update(window);
        return window;
    }

    private static void Update(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            BindingUpdateQueue.Flush();
        }
    }

    private static T Find<T>(IUIComponent root) where T : class
    {
        if (root is T found)
        {
            return found;
        }

        return root.VisualChildren.OfType<IUIComponent>().Select(Find<T>).FirstOrDefault(x => x != null);
    }

    private static ContentPresenter Load(string template)
    {
        var result = AumlLoader.Load(
            $"<ContentPresenter {Namespaces}><ContentPresenter.ContentTemplate>{template}</ContentPresenter.ContentTemplate></ContentPresenter>");
        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        return (ContentPresenter)result.Root;
    }

    // The template's trigger outranks what its markup binds on the same property, and lets it go again.
    [Test]
    public void ADataTemplatesTrigger_OutranksItsOwnBinding()
    {
        var control = Load(
            "<DataTemplate><TextBlock x:Name=\"Label\" Text=\"{Binding Name}\"/>" +
            "<DataTemplate.Triggers><DataTrigger Binding=\"{Binding Name}\" Value=\"{x:Null}\">" +
            "<Setter TargetName=\"Label\" Property=\"Text\" Value=\"none\"/></DataTrigger></DataTemplate.Triggers>" +
            "</DataTemplate>");
        var item = new Item();
        control.Content = item;
        var window = Shown(control);
        var label = Find<TextBlock>(control);
        var none = label?.Text;
        item.Name = "Anna";
        Update(window);
        var named = label?.Text;
        item.Name = null;
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.EqualTo("none"), "the trigger's value while the name is null");
            Assert.That(named, Is.EqualTo("Anna"), "the binding's once the trigger lets go");
            Assert.That(label?.Text, Is.EqualTo("none"), "the trigger's again");
        });
    }

    // A control writing its own current value into a property its template binds leaves the binding alive.
    [Test]
    public void ACurrentValueWritten_LeavesTheTemplatesBindingAlive()
    {
        var control = Load("<DataTemplate><TextBlock x:Name=\"Label\" Text=\"{Binding Name}\"/></DataTemplate>");
        var item = new Item { Name = "Anna" };
        control.Content = item;
        var window = Shown(control);
        var label = Find<TextBlock>(control);
        label.SetCurrentValue(TextBlock.TextProperty, "typed");
        item.Name = "Boris";
        Update(window);

        Assert.That(label.Text, Is.EqualTo("Boris"));
    }

    // A binding's TargetNullValue as a resource is looked up from the bound element: the template's own resources.
    [Test]
    public void ABindingsTargetNullValue_IsAResourceOfTheTemplatesTree()
    {
        var control = Load(
            "<DataTemplate><Border><ResourceContext.Resources><SolidColorBrush x:Key=\"NoPaint\" Color=\"Red\"/>" +
            "</ResourceContext.Resources>" +
            "<Rectangle Fill=\"{Binding Paint, TargetNullValue={ResourceReference NoPaint}}\"/>" +
            "</Border></DataTemplate>");
        var item = new Painted();
        control.Content = item;
        var window = Shown(control);
        var shape = Find<Adamantium.UI.Controls.Shapes.Rectangle>(control);
        var unpainted = (shape?.Fill as Adamantium.UI.Core.Media.SolidColorBrush)?.Color;
        item.Paint = Adamantium.UI.Core.Media.Brushes.Green;
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(unpainted, Is.EqualTo(Adamantium.Mathematics.Colors.Red), "the template's resource for null");
            Assert.That(shape?.Fill, Is.SameAs(Adamantium.UI.Core.Media.Brushes.Green), "the bound value");
        });
    }

    private sealed class Task : INotifyPropertyChanged
    {
        private bool done;
        private bool locked;
        private object size;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Done
        {
            get => done;
            set
            {
                done = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Done)));
            }
        }

        public bool Locked
        {
            get => locked;
            set
            {
                locked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Locked)));
            }
        }

        public object Size
        {
            get => size;
            set
            {
                size = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size)));
            }
        }
    }

    private const string LockedBox =
        "<DataTemplate><CheckBox x:Name=\"Box\" IsChecked=\"{Binding Done}\"/>" +
        "<DataTemplate.Triggers><DataTrigger Binding=\"{Binding Locked}\" Value=\"True\">" +
        "<Setter TargetName=\"Box\" Property=\"IsChecked\" Value=\"False\"/></DataTrigger></DataTemplate.Triggers>" +
        "</DataTemplate>";

    // A trigger's value over a two-way binding of the template is not written back into the source, and once the trigger
    // lets go the source's value shows again.
    [Test]
    public void ATriggersValue_IsNotWrittenBackThroughTheTemplatesBinding()
    {
        var control = Load(LockedBox);
        var task = new Task { Done = true };
        control.Content = task;
        var window = Shown(control);
        var box = Find<CheckBox>(control);
        task.Locked = true;
        Update(window);
        var locked = (box.IsChecked, task.Done);
        task.Locked = false;
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(locked, Is.EqualTo(((bool?)false, true)), "the trigger shows; the source keeps its value");
            Assert.That(box.IsChecked, Is.True, "the source's value again");
        });
    }

    // The control's own value written while a trigger shows over the binding goes into the binding's slot: once the
    // trigger lets go, the source still reaches the property.
    [Test]
    public void ACurrentValueWrittenUnderATrigger_LeavesTheBindingAlive()
    {
        var control = Load(LockedBox);
        var task = new Task { Done = true, Locked = true };
        control.Content = task;
        var window = Shown(control);
        var box = Find<CheckBox>(control);
        box.SetCurrentValue(Adamantium.UI.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        task.Locked = false;
        task.Done = false;
        Update(window);

        Assert.That(box.IsChecked, Is.False);
    }

    // The control's own value written before the binding has written anything leaves the binding alive too.
    [Test]
    public void ACurrentValueWrittenBeforeTheBindingWrote_LeavesItAlive()
    {
        var control = Load("<DataTemplate><Border Width=\"{Binding Size}\"/></DataTemplate>");
        var task = new Task();
        control.Content = task;
        var window = Shown(control);
        var border = Find<Adamantium.UI.Controls.Decorators.Border>(control);
        border.SetCurrentValue(Adamantium.UI.Controls.Decorators.Border.WidthProperty, 50d);
        task.Size = 20d;
        Update(window);

        Assert.That(border.Width, Is.EqualTo(20));
    }

    // A fallback resource found nowhere is null: the bound value going null takes the old one away.
    [Test]
    public void AFallbackResourceFoundNowhere_IsNull()
    {
        var control = Load(
            "<DataTemplate><Rectangle Fill=\"{Binding Paint, TargetNullValue={ResourceReference NoSuchPaint}}\"/></DataTemplate>");
        var item = new Painted { Paint = Adamantium.UI.Core.Media.Brushes.Green };
        control.Content = item;
        var window = Shown(control);
        var shape = Find<Adamantium.UI.Controls.Shapes.Rectangle>(control);
        item.Paint = null;
        Update(window);

        Assert.That(shape.Fill, Is.Not.SameAs(Adamantium.UI.Core.Media.Brushes.Green));
    }

    private sealed class Painted : INotifyPropertyChanged
    {
        private Adamantium.UI.Core.Media.Brush paint;

        public event PropertyChangedEventHandler PropertyChanged;

        public Adamantium.UI.Core.Media.Brush Paint
        {
            get => paint;
            set
            {
                paint = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Paint)));
            }
        }
    }
}
