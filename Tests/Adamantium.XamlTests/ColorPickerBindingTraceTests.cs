using System.Collections.Generic;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A template's parts bind to their control with <c>{Ancestor}</c> while the template is still being put
/// together: a part that has a parent but no root yet has not MISSED its ancestor, it simply is not there yet.</summary>
[TestFixture]
public class ColorPickerBindingTraceTests
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

    [Test]
    public void BuildingThePicker_RaisesNoBindingAlarm()
    {
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            var picker = new ColorPicker
            {
                SelectedColor = new Color(255, 255, 255, 255),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var window = new Window { Width = 800, Height = 500, Content = picker };
            for (var i = 0; i < 4; i++)
            {
                Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
                Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
            }

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(picker.Hex, Is.EqualTo("#FFFFFFFF"), "the fields are not bound to the picker at all");
            });
        }
        finally
        {
            BindingTrace.Sink = null;
        }
    }

    // The flyout builds its picker on first open, from the popup's own template - the order the alarm was seen in.
    [Test]
    public void OpeningTheFlyout_RaisesNoBindingAlarm()
    {
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            var button = new ColorPickerButton
            {
                Width = 120,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var window = new Window { Width = 800, Height = 500, Content = button };
            Settle(window);

            button.IsOpen = true;
            Settle(window);

            var picker = ((Popup)button.GetTemplateChild("PART_Popup")).Child as IUIComponent;
            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(Texts(picker), Has.Member("#FFFFFFFF"), "the hex field is not bound to the picker");
            });
        }
        finally
        {
            BindingTrace.Sink = null;
        }
    }

    private static List<string> Texts(IUIComponent root)
    {
        var texts = new List<string>();
        if (root is TextBox box)
        {
            texts.Add(box.Text);
        }

        if (root != null)
        {
            foreach (var child in root.VisualChildren)
            {
                texts.AddRange(Texts(child));
            }
        }

        return texts;
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }
}
