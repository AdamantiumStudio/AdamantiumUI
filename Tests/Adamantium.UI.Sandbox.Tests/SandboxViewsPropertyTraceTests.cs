using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.Resources;
using Adamantium.UI.Sandbox.Views;
using Adamantium.UI.Themes.EditorProTheme;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>
/// Every view the sandbox ships, built under every theme, sets no value by a name its element has no property for. Such a
/// value is dropped and reported rather than shown - an empty spot on screen that nobody notices - so the report is
/// what fails here.
/// </summary>
[TestFixture]
public class SandboxViewsPropertyTraceTests
{
    private static void Use(string name)
    {
        var app = name switch
        {
            "Fluent" => HeadlessApplication.Start<Fluent>(),
            "EditorPro" => HeadlessApplication.Start<EditorPro>(),
            _ => HeadlessApplication.Start<MacOs>()
        };
        app.ThemeManager.AddStyleSet<RibbonShellStyleSet>();
        app.ResourceManager.AddSource(new ModuleResources(), typeof(ModuleIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(RibbonShellIcons), ResourceScope.Global);
    }

    private static IEnumerable<Type> ViewTypes() =>
        typeof(RibbonShellView).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(MeasurableUIComponent).IsAssignableFrom(type))
            .Where(type => !typeof(Window).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(type => type.FullName);

    private static bool TryBuild(Type type)
    {
        try
        {
            var view = (MeasurableUIComponent)Activator.CreateInstance(type);
            var window = new Window { Width = 1280, Height = 720, ClientWidth = 1280, ClientHeight = 720, Content = view };
            for (var i = 0; i < 3; i++)
            {
                window.ApplyCurrentTheme();
                Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
                Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
                window.LayoutPopups();
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [TestCase("Fluent")]
    [TestCase("EditorPro")]
    [TestCase("MacOs")]
    public void NoViewUnderTheTheme_SetsAPropertyItDoesNotHave(string theme)
    {
        Use(theme);
        var reports = new List<string>();
        Action<string> collect = reports.Add;
        PropertyTrace.Sink += collect;

        try
        {
            var built = ViewTypes().Count(TryBuild);

            Assert.That(built, Is.GreaterThan(50), "the sweep reached the views");
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
