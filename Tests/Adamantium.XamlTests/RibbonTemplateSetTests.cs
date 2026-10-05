using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A ribbon bound to view models of several kinds, its labels from one set of templates and each tab's groups
/// from another, both picked by the view model's type: every tab is labelled and filled by its own kind, and opening
/// another tab swaps what the groups area shows.</summary>
[TestFixture]
public class RibbonTemplateSetTests
{
    public class TabViewModel
    {
        public string Title { get; init; }
    }

    public class TerrainViewModel : TabViewModel
    {
    }

    public class WaterViewModel : TabViewModel
    {
    }

    private object _previous;

    [SetUp]
    public void UseAnApplicationWithATheme()
    {
        var current = typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current));
        _previous = current.GetValue(null);
        var app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        current.SetValue(null, app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        app.ThemeManager = themes;
        ((FakeContext)app.UIContext).ThemeEngine = themes;
        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    [TearDown]
    public void PutTheApplicationBack()
    {
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _previous);
    }

    [Test]
    public void EachTab_IsLabelledAndFilledByItsKind()
    {
        var ribbon = new Ribbon
        {
            ItemTemplateSelector = Set((typeof(TabViewModel), () =>
            {
                var label = new TextBlock();
                label.SetBinding(nameof(TextBlock.Text), new Binding(nameof(TabViewModel.Title)));
                return label;
            })),
            ContentTemplateSelector = Set(
                (typeof(TerrainViewModel), () => new TextBlock { Text = "terrain groups" }),
                (typeof(WaterViewModel), () => new TextBlock { Text = "water groups" })),
            ItemsSource = new ObservableCollection<TabViewModel>
            {
                new TerrainViewModel { Title = "Terrain" },
                new WaterViewModel { Title = "Water" }
            }
        };
        var window = new Window { Width = 800, Height = 300, Content = ribbon };
        Update(window);

        var first = Texts(ribbon);
        ribbon.SelectedIndex = 1;
        Update(window);
        var second = Texts(ribbon);

        Assert.That(first, Is.SupersetOf(new[] { "Terrain", "Water", "terrain groups" }).And.No.Contains("water groups"));
        Assert.That(second, Does.Contain("water groups").And.No.Contains("terrain groups"));
    }

    private static DataTemplateSet Set(params (System.Type Type, System.Func<TextBlock> Make)[] templates)
    {
        var set = new DataTemplateSet();
        foreach (var (type, make) in templates)
        {
            set.Templates.Add(new DataTemplate(() => new TemplateResult { RootComponent = make() }) { DataType = type });
        }

        return set;
    }

    private static void Update(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static List<string> Texts(IUIComponent root)
    {
        var texts = new List<string>();
        Collect(root);
        return texts;

        void Collect(IUIComponent node)
        {
            if (node is TextBlock { Text: { Length: > 0 } text })
            {
                texts.Add(text);
            }

            foreach (var child in node.VisualChildren)
            {
                Collect(child);
            }
        }
    }
}
