using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// The clear button of a TextBox under each theme: there when asked for and there is text to clear, gone otherwise, and
/// one press empties the box.
/// </summary>
[TestFixture]
public class TextBoxClearButtonThemeTests
{
    private FakeApp _app;
    private ThemeManager _themes;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(Theme theme)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        _themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = _themes;
        ((FakeContext)_app.UIContext).ThemeEngine = _themes;

        _themes.AddTheme(theme.Name, theme);
        _themes.SetTheme(theme);
    }

    private static TextBox Built(TextBox box)
    {
        box.ApplyCurrentTheme();
        Settle(box);
        return box;
    }

    private static void Settle(TextBox box)
    {
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(box);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        box.Measure(new Size(240, 40));
        box.Arrange(new Rect(0, 0, 240, 40));
    }

    private static ButtonBase ClearButton(TextBox box) => box.GetTemplateChild("PART_ClearButton") as ButtonBase;

    private static Theme MacOs() => new Adamantium.UI.Themes.MacOsTheme.MacOs();

    private static Theme Fluent() => new Adamantium.UI.Themes.FluentTheme.Fluent();

    private static Theme EditorPro() => new Adamantium.UI.Themes.EditorProTheme.EditorPro();

    [Test]
    public void ItShowsWhileThereIsTextUnderMacOs() => ShowsWhileThereIsText(MacOs());

    [Test]
    public void ItShowsWhileThereIsTextUnderFluent() => ShowsWhileThereIsText(Fluent());

    [Test]
    public void ItShowsWhileThereIsTextUnderEditorPro() => ShowsWhileThereIsText(EditorPro());

    private void ShowsWhileThereIsText(Theme theme)
    {
        Use(theme);
        var box = Built(new TextBox { ShowsClearButton = true });
        var button = ClearButton(box);
        Assert.That(button, Is.Not.Null, "the theme names the part the box listens to");
        Assert.That(button.Visibility, Is.EqualTo(Visibility.Collapsed), "an empty box has nothing to clear");

        box.Text = "terrain";
        Settle(box);
        Assert.That(button.Visibility, Is.EqualTo(Visibility.Visible));

        box.Text = string.Empty;
        Settle(box);
        Assert.That(button.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ItStaysAwayUnlessAskedForUnderMacOs() => StaysAwayUnlessAskedFor(MacOs());

    [Test]
    public void ItStaysAwayUnlessAskedForUnderFluent() => StaysAwayUnlessAskedFor(Fluent());

    [Test]
    public void ItStaysAwayUnlessAskedForUnderEditorPro() => StaysAwayUnlessAskedFor(EditorPro());

    private void StaysAwayUnlessAskedFor(Theme theme)
    {
        Use(theme);
        var plain = Built(new TextBox { Text = "terrain" });
        var readOnly = Built(new TextBox { Text = "terrain", ShowsClearButton = true, IsReadOnly = true });

        Assert.Multiple(() =>
        {
            Assert.That(ClearButton(plain).Visibility, Is.EqualTo(Visibility.Collapsed), "a box that did not ask for it");
            Assert.That(ClearButton(readOnly).Visibility, Is.EqualTo(Visibility.Collapsed),
                "a read-only box cannot be cleared");
        });
    }

    [Test]
    public void ItSaysWhatTheBoxIsToldUnderMacOs() => SaysWhatTheBoxIsTold(MacOs());

    [Test]
    public void ItSaysWhatTheBoxIsToldUnderFluent() => SaysWhatTheBoxIsTold(Fluent());

    [Test]
    public void ItSaysWhatTheBoxIsToldUnderEditorPro() => SaysWhatTheBoxIsTold(EditorPro());

    // The theme's general word, until the box is told what clearing it means here.
    private void SaysWhatTheBoxIsTold(Theme theme)
    {
        Use(theme);
        var plain = Built(new TextBox { Text = "terrain", ShowsClearButton = true });
        var told = Built(new TextBox { Text = "terrain", ShowsClearButton = true, ClearButtonToolTip = "Clear the search" });

        Assert.Multiple(() =>
        {
            Assert.That(ClearButton(plain).ToolTip, Is.EqualTo(Adamantium.UI.Themes.Localization.TextBoxStrings.Clear));
            Assert.That(ClearButton(told).ToolTip, Is.EqualTo("Clear the search"));
        });
    }

    [Test]
    public void OnePressEmptiesTheBoxUnderMacOs() => OnePressEmptiesTheBox(MacOs());

    [Test]
    public void OnePressEmptiesTheBoxUnderFluent() => OnePressEmptiesTheBox(Fluent());

    [Test]
    public void OnePressEmptiesTheBoxUnderEditorPro() => OnePressEmptiesTheBox(EditorPro());

    private void OnePressEmptiesTheBox(Theme theme)
    {
        Use(theme);
        var box = Built(new TextBox { Text = "terrain", ShowsClearButton = true });

        ClearButton(box).PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.Empty);
            Assert.That(ClearButton(box).Focusable, Is.False, "the press leaves the focus in the box");
        });
    }
}
