using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>From a resource key or a class made from markup the editor goes to the markup that declares it; a key renamed
/// in the editor is known at once, with no build; renaming a key changes it everywhere it is written.</summary>
[TestFixture]
public class AumlResourceNavigationTests
{
    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">""";

    private const string Icons = """
        <ResourceDictionary xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">
            <DrawingImage x:Key="SelectIcon"><GeometryDrawing Geometry="M0,0 L1,1"/></DrawingImage>
            <SolidColorBrush x:Key="SelectInk" Color="Red"/>
        </ResourceDictionary>
        """;

    private const string Uses = """
        <View xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">
            <!-- {ObservableResource SelectIcon} in a comment is not a use -->
            <RibbonButton Icon="{ObservableResource SelectIcon}"/>
            <RibbonButton Icon="{ResourceReference Key=SelectIcon}"/>
            <Border Background="{ObservableResource SelectInk}"/>
        </View>
        """;

    private AumlTypeModel _model;
    private string _project;
    private string _icons;
    private string _uses;

    [OneTimeSetUp]
    public void BuildModel()
    {
        _project = Path.Combine(Path.GetTempPath(), $"aumlnavigation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_project);
        File.WriteAllText(Path.Combine(_project, "Probe.csproj"), "<Project/>");
        _icons = Path.Combine(_project, "HomeIcons.auml");
        _uses = Path.Combine(_project, "UsesIcons.auml");
        File.WriteAllText(_icons, Icons);
        File.WriteAllText(_uses, Uses);

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        _model = AumlTypeModel.Build(byName.Values);
        _model.TrackMarkupIn(_project);
        _model.RegisterViews(Directory.GetFiles(_project, "*.auml"), _model.Compilation.AssemblyName, _project);
        _model.RegisterResourceKeys(Directory.GetFiles(_project, "*.auml"));
    }

    [OneTimeTearDown]
    public void RemoveProject()
    {
        Directory.Delete(_project, true);
    }

    [TestCase("""<Border Background="{ObservableResource Select|Icon}"/>""")]
    [TestCase("""<Border Background="{ResourceReference Key=SelectIc|on}"/>""")]
    public void AKey_GoesToWhereItIsDeclared(string body)
    {
        var (text, caret) = Marked(Root + body + "</Window>");

        var location = new DefinitionEngine(_model).Definition(text, caret);

        Assert.That(location, Is.Not.Null);
        Assert.That(location.FilePath, Is.EqualTo(Path.GetFullPath(_icons)).IgnoreCase);
        Assert.That((location.StartLine, location.StartCharacter), Is.EqualTo((1, 25)));
    }

    [TestCase("""<ResourceLink Source="Home|Icons"/>""")]
    [TestCase("""<Border ResourceContext.Source="{ResourceLink Source={x:Type Home|Icons}}"/>""")]
    public void AClassMadeFromMarkup_GoesToItsMarkup(string body)
    {
        var (text, caret) = Marked(Root + body + "</Window>");

        var location = new DefinitionEngine(_model).Definition(text, caret);

        Assert.That(location?.FilePath, Is.EqualTo(Path.GetFullPath(_icons)).IgnoreCase);
    }

    [Test]
    public void AKeyRenamedInTheEditor_IsKnownAtOnce_AndAHalfTypedFileKeepsIt()
    {
        var live = Path.Combine(_project, "LiveIcons.auml");
        _model.UpdateMarkup(live, Icons.Replace("SelectIcon", "LiveIcon"));
        _model.UpdateMarkup(live, Icons.Replace("SelectIcon", "PickIcon"));
        var renamed = _model.ResourceKeys.Select(k => k.Key).ToList();
        _model.UpdateMarkup(live, Icons.Replace("SelectIcon", "PickIcon")[..60]);
        var halfTyped = _model.ResourceKeys.Select(k => k.Key).ToList();
        _model.RemoveMarkup(live);

        Assert.That(renamed, Does.Contain("PickIcon").And.Not.Contain("LiveIcon"));
        Assert.That(halfTyped, Does.Contain("PickIcon"));
        Assert.That(_model.ResourceKeys.Select(k => k.Key), Does.Not.Contain("PickIcon"));
    }

    [Test]
    public void AKey_IsFoundEverywhereItIsWritten_ButNotInAComment()
    {
        var usages = new ResourceKeyUsages(_model, _ => null).Find("SelectIcon");

        Assert.That(usages.Select(u => (Path.GetFileName(u.FilePath), u.StartLine)),
            Is.EquivalentTo(new[] { ("HomeIcons.auml", 1), ("UsesIcons.auml", 2), ("UsesIcons.auml", 3) }));
    }

    [Test]
    public void AnOpenDocument_IsReadAsTheEditorHoldsIt()
    {
        var edited = Uses.Replace("""<Border Background="{ObservableResource SelectInk}"/>""",
            """<Border Background="{ObservableResource SelectIcon}"/>""");

        var usages = new ResourceKeyUsages(_model, f => string.Equals(f, Path.GetFullPath(_uses), StringComparison.OrdinalIgnoreCase) ? edited : null)
            .Find("SelectIcon");

        Assert.That(usages.Count(u => Path.GetFileName(u.FilePath) == "UsesIcons.auml"), Is.EqualTo(3));
    }

    [TestCase("""<Border Background="{ThemeResource Accent|Fill}"/>""", false)]
    [TestCase("""<Border Background="{ObservableResource Nowhere|Key}"/>""", false)]
    [TestCase("""<Border Background="{ObservableResource Select|Icon}"/>""", true)]
    public void OnlyAKeyDeclaredInMarkup_CanBeRenamed(string body, bool renameable)
    {
        var (text, caret) = Marked(Root + body + "</Window>");

        var key = new ResourceKeyUsages(_model, _ => null).Renameable(text, caret, out var why);

        Assert.That(key != null, Is.EqualTo(renameable), why);
        Assert.That(why == null, Is.EqualTo(renameable));
    }

    [Test]
    public void AKeyAnotherProjectDeclaresToo_IsWarnedOn_GoesToItsOwn_AndIsNotRenamed()
    {
        var theme = Path.Combine(Path.GetTempPath(), $"aumltheme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(theme);
        var themeIcons = Path.Combine(theme, "ThemeIcons.auml");
        try
        {
            _model.TrackMarkupIn(theme);
            _model.UpdateMarkup(themeIcons, Icons);
            var (text, caret) = Marked(Root + """<Border Background="{ObservableResource Select|Icon}"/></Window>""");

            var warnings = ResourceKeyShadowCheck.Check(_icons, File.ReadAllText(_icons), _model);
            var definition = new DefinitionEngine(_model).Definition(text, caret, _uses);
            var renamed = new ResourceKeyUsages(_model, _ => null).Renameable(text, caret, out var why);

            Assert.That(warnings.Select(w => w.Message), Has.Some.StartsWith("'SelectIcon' is declared in").And.Some.Contains("ThemeIcons.auml"));
            Assert.That(warnings.All(w => w.IsWarning));
            Assert.That(definition?.FilePath, Is.EqualTo(Path.GetFullPath(_icons)).IgnoreCase);
            Assert.That(renamed, Is.Null);
            Assert.That(why, Does.Contain("ThemeIcons.auml").And.Contain("HomeIcons.auml"));
        }
        finally
        {
            _model.RemoveMarkup(themeIcons);
            Directory.Delete(theme, true);
        }
    }

    [TestCase("Pick.Icon-2", true)]
    [TestCase("2Icon", false)]
    [TestCase("Pick Icon", false)]
    [TestCase("", false)]
    public void ANewName_MustBeAKey(string name, bool valid)
    {
        Assert.That(ResourceKeyUsages.IsKeyName(name), Is.EqualTo(valid));
    }

    private static (string Text, int Caret) Marked(string marked)
    {
        var caret = marked.IndexOf('|');
        return (marked.Remove(caret, 1), caret);
    }
}
