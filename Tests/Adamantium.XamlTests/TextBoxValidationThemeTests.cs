using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A text box whose source says its value is wrong wears the critical color on its border - its rule, in
/// Graphite - and gives it back once the source takes the error back.</summary>
[TestFixture]
public class TextBoxValidationThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private sealed class Form : INotifyDataErrorInfo
    {
        private string _error;

        public string Name { get; set; } = "1Game";

        public bool HasErrors => _error != null;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public IEnumerable GetErrors(string propertyName) =>
            propertyName == nameof(Name) && _error != null ? new[] { _error } : Array.Empty<string>();

        public void Say(string error)
        {
            _error = error;
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Name)));
        }
    }

    private void Use(string name)
    {
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static void Lay(TextBox box)
    {
        box.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(box);
        BindingUpdateQueue.Flush();
        box.Measure(new Size<double>(200, 40));
        box.Arrange(new Rect(0, 0, 200, 40));
    }

    private static object Mark(TextBox box, string theme) => theme == "Graphite"
        ? ((Border)box.GetTemplateChild("Rule")).Background
        : ((Border)box.GetTemplateChild("PART_Border")).BorderBrush;

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AWrongValue_IsMarked_AndUnmarkedWhenRight(string theme)
    {
        Use(theme);
        var form = new Form();
        var box = new TextBox { DataContext = form };
        box.SetBinding("Text", new Binding("Name") { Mode = BindingMode.TwoWay });
        Lay(box);
        var plain = Mark(box, theme);

        form.Say("Use letters, not starting with a digit.");
        Lay(box);
        var marked = Mark(box, theme);
        form.Say(null);
        Lay(box);

        Assert.Multiple(() =>
        {
            Assert.That(marked, Is.Not.EqualTo(plain), "marked while wrong");
            Assert.That(Mark(box, theme), Is.EqualTo(plain), "unmarked once right");
        });
    }
}
