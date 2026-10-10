using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A source offers only what it can take: a <see cref="Type"/>-valued property marked <c>[TypeOf]</c> the
/// classes derived from its base - written in C# or generated from markup, in every form the value is written in - and an
/// image property the picture files of the project.</summary>
[TestFixture]
public class AumlSourceCompletionTests
{
    private const string Probe = """
        namespace Probe;

        public class ProbeBrushes : Adamantium.UI.Core.Resources.ResourceDictionary { }

        public class ProbeButtons : Adamantium.UI.Core.Resources.StyleSet { }

        public class ProbeRails { }
        """;

    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe">""";

    private const string BareRoot =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">""";

    private static readonly string[] Dictionaries = ["ProbeBrushes", "HomeBrushes"];

    private static readonly string[] StyleSets = ["ProbeButtons", "HomeStyles"];

    private CompletionEngine _engine;
    private string _probeFile;
    private string _project;

    [OneTimeSetUp]
    public void BuildModel()
    {
        _probeFile = Path.Combine(Path.GetTempPath(), $"aumlprobe-{Guid.NewGuid():N}.cs");
        File.WriteAllText(_probeFile, Probe);

        _project = Path.Combine(Path.GetTempPath(), $"aumlsources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_project, "Icons"));
        Directory.CreateDirectory(Path.Combine(_project, "Docs"));
        File.WriteAllText(Path.Combine(_project, "Probe.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_project, "logo.png"), "");
        File.WriteAllText(Path.Combine(_project, "notes.txt"), "");
        File.WriteAllText(Path.Combine(_project, "Icons", "close.ico"), "");
        File.WriteAllText(Path.Combine(_project, "Icons", "close.svg"), "");
        File.WriteAllText(Path.Combine(_project, "Docs", "readme.txt"), "");
        File.WriteAllText(Path.Combine(_project, "HomeBrushes.auml"),
            """<ResourceDictionary xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions"/>""");
        File.WriteAllText(Path.Combine(_project, "HomeStyles.auml"),
            """<StyleSet xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions"/>""");
        File.WriteAllText(Path.Combine(_project, "HomeIcons.auml"),
            """
            <ResourceDictionary xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">
                <DrawingImage x:Key="SelectIcon"><GeometryDrawing Geometry="M0,0 L1,1"/></DrawingImage>
                <SolidColorBrush x:Key="SelectInk" Color="Red"/>
            </ResourceDictionary>
            """);

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        var model = AumlTypeModel.Build(byName.Values, [_probeFile]);
        model.RegisterViews(Directory.GetFiles(_project, "*.auml"), model.Compilation.AssemblyName, _project);
        model.RegisterResourceKeys(Directory.GetFiles(_project, "*.auml"));
        _engine = new CompletionEngine(model);
    }

    [OneTimeTearDown]
    public void RemoveProbe()
    {
        File.Delete(_probeFile);
        Directory.Delete(_project, true);
    }

    [TestCase("""<ResourceLink Source="|"/>""")]
    [TestCase("""<Border ResourceContext.Source="{ResourceLink Source=|}"/>""")]
    [TestCase("""<Border ResourceContext.Source="{ResourceLink Source={x:Type |}}"/>""")]
    public void AResourceLink_OffersOnlyResourceDictionaries(string body)
    {
        var labels = Labels(body, BareRoot);

        Assert.That(labels, Is.SupersetOf(Dictionaries));
        Assert.That(labels, Has.None.AnyOf([..StyleSets, "ProbeRails", "ResourceDictionary", "Border"]));
    }

    [TestCase("""<StyleInclude Source="|"/>""")]
    [TestCase("""<StyleInclude Source="{x:Type |}"/>""")]
    public void AStyleInclude_OffersOnlyStyleSets(string body)
    {
        var labels = Labels(body, BareRoot);

        Assert.That(labels, Is.SupersetOf(StyleSets));
        Assert.That(labels, Has.None.AnyOf([..Dictionaries, "ProbeRails", "StyleSet", "Border"]));
    }

    [Test]
    public void AStyleIncludeWithAPrefix_OffersOnlyTheStyleSetsOfThatNamespace() =>
        Assert.That(Labels("""<StyleInclude Source="local:|"/>"""), Is.EquivalentTo(new[] { "ProbeButtons" }));

    [TestCase("""<Border Background="{ObservableResource Select|}"/>""", "SelectInk", "SelectIcon")]
    [TestCase("""<Border Background="{ResourceReference Key=Select|}"/>""", "SelectInk", "SelectIcon")]
    [TestCase("""<Image Source="{ObservableResource Select|}"/>""", "SelectIcon", "SelectInk")]
    public void AResourceKey_IsOfferedWhereWhatItHoldsFits(string body, string offered, string hidden)
    {
        var labels = Labels(body, BareRoot);

        Assert.That(labels, Does.Contain(offered));
        Assert.That(labels, Does.Not.Contain(hidden));
    }

    [Test]
    public void AResourceKeyOfAnObjectProperty_IsOfferedWhateverItHolds() =>
        Assert.That(Labels("""<Button Content="{ObservableResource Select|}"/>""", BareRoot), Is.SupersetOf(new[] { "SelectIcon", "SelectInk" }));

    [Test]
    public void TheThemesPaletteKeys_AreOfferedForABrush() =>
        Assert.That(Labels("""<Border Background="{ObservableResource TextFillColorPri|}"/>""", BareRoot), Does.Contain("TextFillColorPrimary"));

    [Test]
    public void AStartupTheme_OffersTheThemesTheBuildFinds()
    {
        var labels = Labels("""<ApplicationBlueprint StartupTheme="|"/>""", BareRoot);

        Assert.That(labels, Is.SupersetOf(new[] { "Fluent", "Graphite", "MacOs" }));
        Assert.That(labels, Has.None.AnyOf("Border", "ProbeButtons", "Theme"));
    }

    [Test]
    public void ATypePropertyWithNoBase_OffersAnyType() =>
        Assert.That(Labels("""<ControlTemplate TargetType="|"/>""", BareRoot), Is.SupersetOf(new[] { "Border", "ProbeRails" }));

    [TestCase("""<Image Source="|"/>""")]
    [TestCase("""<ImageBrush Source="|"/>""")]
    [TestCase("""<BitmapImage UriSource="|"/>""")]
    public void AnImageSource_OffersPicturesAndTheFoldersHoldingThem(string body) =>
        Assert.That(Labels(body, BareRoot), Is.EquivalentTo(new[] { "Icons/", "logo.png" }));

    [Test]
    public void AnImageSourceInAFolder_OffersItsPictures() =>
        Assert.That(Labels("""<Image Source="Icons/|"/>""", BareRoot), Is.EquivalentTo(new[] { "close.ico" }));

    private IReadOnlyList<string> Labels(string body, string root = Root)
    {
        var marked = root + body + "</Window>";
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        return _engine.Complete(text, caret, Path.Combine(_project, "Main.auml")).Select(i => i.Label).ToList();
    }
}
