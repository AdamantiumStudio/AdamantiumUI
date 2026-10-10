using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.GraphiteTheme;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A value set by a name the element has no property for is dropped - and reported, not swallowed. Here every control
/// the library ships is built under every theme, and a single report fails: a theme that writes into a name its part
/// does not have says nothing on screen, only an empty spot where something should have been.
/// </summary>
[TestFixture]
public class PropertyTraceThemeSweepTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(string name)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        Theme theme = name switch
        {
            "Fluent" => new Fluent(),
            "Graphite" => new Graphite(),
            _ => new MacOs()
        };
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static IEnumerable<Type> ControlTypes() =>
        typeof(TitleBar).Assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition)
            .Where(type => typeof(MeasurableUIComponent).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(type => type.FullName);

    private static bool TryBuild(Type type)
    {
        try
        {
            var control = (MeasurableUIComponent)Activator.CreateInstance(type);
            control.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
            control.Measure(new Size(400, 300));
            control.Arrange(new Rect(0, 0, 400, 300));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void NoControlUnderTheTheme_SetsAPropertyItDoesNotHave(string theme)
    {
        Use(theme);
        var reports = new List<string>();
        Action<string> collect = reports.Add;
        PropertyTrace.Sink += collect;

        try
        {
            var built = ControlTypes().Count(TryBuild);

            Assert.That(built, Is.GreaterThan(100), "the sweep reached the controls");
            Assert.That(reports, Is.Empty,
                "values dropped for want of a property:" + Environment.NewLine +
                string.Join(Environment.NewLine, reports.Distinct()));
        }
        finally
        {
            PropertyTrace.Sink -= collect;
        }
    }
}
