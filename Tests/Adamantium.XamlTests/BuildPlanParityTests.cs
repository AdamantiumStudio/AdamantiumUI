using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Markup.Building;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// The generator plans a document through Roslyn's view of the types, the loader through reflection. The designer and
/// hot reload build what the loader planned, so the two plans must be the same statement for statement - checked here
/// over every file of the shipped themes, with the plan printed as the generator prints it - and then run, every
/// statement of it.
/// </summary>
[TestFixture]
public class BuildPlanParityTests
{
    private static readonly string SourcePath = AumlCodegenHarness.PathOf("MainWindow.auml");

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
    }

    private static IEnumerable<string> ThemeFiles()
    {
        var root = ThemesRoot();
        return Directory.EnumerateFiles(root, "*.auml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private const string ViewHeader =
        "<View x:Namespace=\"Test.App\" xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">";

    private static readonly string[] Views =
    [
        ViewHeader + "<Grid RowDefinitions=\"Auto,*\"><TextBlock x:Name=\"Title\" Text=\"{Binding Name}\" Grid.Row=\"1\"/>" +
        "<Border x:Load=\"{Binding Show}\"><TextBlock Text=\"Lazy\"/></Border></Grid></View>",
        ViewHeader + "<ResourceContext.Resources><SolidColorBrush x:Key=\"Accent\" Color=\"Red\"/></ResourceContext.Resources>" +
        "<Button x:Name=\"Run\" Background=\"{ResourceReference Accent}\" Content=\"Run\"><Button.Template>" +
        "<ControlTemplate TargetType=\"Button\"><Border Background=\"{TemplateBinding Background}\" Padding=\"4\"/>" +
        "</ControlTemplate></Button.Template></Button></View>",
        ViewHeader + "<StackPanel><ListBox ItemsSource=\"{Binding Items}\"><ListBox.ItemTemplate><DataTemplate>" +
        "<TextBlock Text=\"{Binding}\" Opacity=\"0.5\"/></DataTemplate></ListBox.ItemTemplate></ListBox>" +
        "<Border Width=\"Auto\" HorizontalAlignment=\"Left\" Visibility=\"{Binding Hidden, Mode=OneWay}\"/></StackPanel></View>",
    ];

    [TestCaseSource(nameof(Views))]
    public void TheLoader_PlansAView_AsTheGeneratorDoes(string auml)
    {
        Compare(auml);
    }

    [TestCaseSource(nameof(ThemeFiles))]
    public void TheLoader_PlansAThemeFile_AsTheGeneratorDoes(string relativePath)
    {
        Compare(File.ReadAllText(Path.Combine(ThemesRoot(), relativePath)));
    }

    private static void Compare(string auml)
    {
        var generated = AumlCodegenHarness.Generate(auml, out var errors);
        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));

        var diagnostics = new List<string>();
        var lowered = AumlLoader.Lower(auml, null, diagnostics, SourcePath);
        Assert.That(lowered?.Plan, Is.Not.Null, string.Join(" | ", diagnostics));

        var printed = new TextGenerator();
        BuildPlanEmitter.Emit(lowered.Plan, printed);

        var mismatch = FirstMismatch(Lines(generated), Lines(printed));
        Assert.That(mismatch, Is.Null, mismatch);
    }

    [TestCaseSource(nameof(ThemeFiles))]
    public void TheLoader_BuildsAThemeFile_StatementByStatement(string relativePath)
    {
        var auml = File.ReadAllText(Path.Combine(ThemesRoot(), relativePath));

        var result = AumlLoader.Load(auml);

        Assert.That(result.Root, Is.Not.Null, string.Join(" | ", result.Diagnostics));
        Assert.That(result.Diagnostics.Where(d => !d.StartsWith("warning:", StringComparison.Ordinal)), Is.Empty);
    }

    private static string FirstMismatch(List<string> generated, List<string> planned)
    {
        var start = generated.IndexOf(planned.FirstOrDefault());
        if (start < 0)
        {
            return $"the loader's first statement is not in the generated code: {planned.FirstOrDefault()}";
        }

        for (var i = 0; i < planned.Count; i++)
        {
            var expected = start + i < generated.Count ? generated[start + i] : "<end of the generated code>";
            if (expected != planned[i])
            {
                return $"statement {i + 1}: the generator wrote{Environment.NewLine}  {expected}{Environment.NewLine}" +
                       $"the loader planned{Environment.NewLine}  {planned[i]}";
            }
        }

        return null;
    }

    private static List<string> Lines(string text)
    {
        return text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static string ThemesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Adamantium.UI.Themes");
            if (Directory.Exists(Path.Combine(candidate, "FluentTheme")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not find Adamantium.UI.Themes above " + AppContext.BaseDirectory);
    }
}
