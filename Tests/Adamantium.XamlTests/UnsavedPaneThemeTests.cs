using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Unsaved work under each theme: a changed pane's tab wears the mark and loses it when saved, and the question
/// before closing has every answer the area wires.</summary>
[TestFixture]
public class UnsavedPaneThemeTests
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
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "EditorPro" => new Adamantium.UI.Themes.EditorProTheme.EditorPro(),
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

    private static void Settle(IUIComponent control)
    {
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        ((IMeasurableComponent)control).Measure(new Size(600, 400));
        ((IMeasurableComponent)control).Arrange(new Rect(0, 0, 600, 400));
    }

    [TestCase("Fluent")]
    [TestCase("EditorPro")]
    [TestCase("MacOs")]
    public void AChangedPane_WearsTheMark_UntilItIsSaved(string theme)
    {
        Use(theme);

        var changed = new Pane { Header = "main.cs", Id = "main", IsDirty = true };
        var saved = new Pane { Header = "other.cs", Id = "other" };
        var group = new PaneGroup();
        group.Items.Add(changed);
        group.Items.Add(saved);
        group.ApplyCurrentTheme();
        Settle(group);

        Assert.Multiple(() =>
        {
            Assert.That((changed.GetTemplateChild("DirtyMark") as IUIComponent)?.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That((saved.GetTemplateChild("DirtyMark") as IUIComponent)?.Visibility, Is.EqualTo(Visibility.Collapsed));
        });

        changed.IsDirty = false;
        Settle(group);

        Assert.That((changed.GetTemplateChild("DirtyMark") as IUIComponent)?.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase("Fluent")]
    [TestCase("EditorPro")]
    [TestCase("MacOs")]
    public void TheQuestion_HasEveryAnswer(string theme)
    {
        Use(theme);

        var question = new UnsavedQuestion { Count = 1, Subject = "main.cs" };
        question.ApplyCurrentTheme();
        Settle(question);

        Assert.Multiple(() =>
        {
            Assert.That(question.GetTemplateChild("PART_Save"), Is.Not.Null, "save");
            Assert.That(question.GetTemplateChild("PART_Discard"), Is.Not.Null, "don't save");
            Assert.That(question.GetTemplateChild("PART_Cancel"), Is.Not.Null, "cancel");
            Assert.That((question.GetTemplateChild("OneLabel") as IUIComponent)?.Visibility, Is.EqualTo(Visibility.Visible),
                "one pane is named");
        });
    }
}
