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
