using System;
using System.Collections.Generic;
using Adamantium.Core.Commands;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.EditorProTheme;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// The themes' overflow menus - commands that no longer fit the quick-access bar or the caption - are menus of data
/// rows. Each row is a MenuItem the menu makes: its words come from the row template, its command and icon from the
/// container style. Their templates used to build a MenuItem of their own, which only worked while a plain template
/// made bare rows.
/// </summary>
[TestFixture]
public class OverflowMenuRowThemeTests
{
    private sealed class Counted : ICommand
    {
        public int Runs { get; private set; }

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => Runs++;

        public void RaiseCanExecuteChanged() { }
    }

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
            "EditorPro" => new EditorPro(),
            _ => new MacOs()
        };
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static T Built<T>(T control, double width, double height) where T : MeasurableUIComponent
    {
        control.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        return control;
    }

    private static MenuItem RowFor(ContextMenu menu, WindowCommand command)
    {
        Assert.That(menu.ItemContainerStyle, Is.Not.Null, "the theme says what its rows do");
        menu.ItemsSource = new List<object> { command };
        var row = menu.ItemContainerGenerator.Realize(0) as MenuItem;
        if (row == null) return null;

        var window = new Window { Width = 240, Height = 32, Content = row };
        for (var i = 0; i < 3; i++)
        {
            window.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }

        return row;
    }

    private static void AssertTheRowCarries(MenuItem row, WindowCommand command)
    {
        Assert.That(row, Is.Not.Null, "a menu row");
        Assert.Multiple(() =>
        {
            Assert.That(row.Header, Is.SameAs(command));
            Assert.That(row.HeaderTemplate, Is.Not.Null, "the theme's words for it");
            Assert.That(row.Command, Is.SameAs(command.Command));
            Assert.That((row.Icon as Path)?.Data, Is.SameAs(command.Icon));
        });
    }

    [TestCase("Fluent")]
    [TestCase("EditorPro")]
    [TestCase("MacOs")]
    public void AQuickAccessOverflowRow_RunsItsCommand_AndShowsItsIcon(string theme)
    {
        Use(theme);
        var bar = Built(new RibbonQuickAccess(), 320, 32);
        var command = new WindowCommand { Label = "Save", IconData = "M0,0 L8,8", Command = new Counted() };

        AssertTheRowCarries(RowFor((ContextMenu)bar.GetTemplateChild("PART_OverflowMenu"), command), command);
    }

    [TestCase("Fluent")]
    [TestCase("EditorPro")]
    public void ACaptionOverflowRow_RunsItsCommand_AndShowsItsIcon(string theme)
    {
        Use(theme);
        var bar = Built(new TitleBar(), 600, 32);
        var command = new WindowCommand { Label = "Ribbon", IconData = "M0,0 L8,8", Command = new Counted() };

        AssertTheRowCarries(RowFor((ContextMenu)bar.GetTemplateChild("PART_RightOverflowMenu"), command), command);
    }
}
