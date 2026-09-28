using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A ContentPresenter given a string builds its own label and binds the label's colour to its own. A presenter
/// placed straight in a page (a rotated tool-strip caption) had no colour of its own, the binding wrote that null over
/// the label's, and the label threw on every frame it was drawn.</summary>
[TestFixture]
public class GeneratedLabelForegroundTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void UseFluent()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static T Find<T>(IUIComponent root) where T : class =>
        root as T ?? root.VisualChildren.Select(Find<T>).FirstOrDefault(found => found != null);

    [Test]
    public void AStringInAPresenterOnAPage_IsLabelledInThePagesColour()
    {
        UseFluent();
        var page = (IUIComponent)AumlLoader.Load(
            "<View xmlns=\"http://adamantium/ui\"><StackPanel><ContentPresenter Content=\"Inspector\"/></StackPanel></View>").Root;
        var window = new VirtualWindow { Content = page, UseCustomChrome = false, ClientWidth = 400, ClientHeight = 300 };
        window.AttachContextAndInitialize(_app.UIContext);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        var label = Find<TextBlock>(Find<Adamantium.UI.Controls.Panels.StackPanel>(page));
        var chain = string.Join(" <- ", Enumerable.Repeat<IUIComponent>(label, 1)
            .Concat(label.GetVisualAncestors())
            .Select(c => $"{c.GetType().Name}:{((Adamantium.UI.Controls.Base.UIComponent)c).Foreground?.ToString() ?? "null"}"));

        Assert.That(label.Foreground, Is.Not.Null, chain);
        Assert.That(label.Foreground, Is.EqualTo(window.Foreground), chain);
    }
}
