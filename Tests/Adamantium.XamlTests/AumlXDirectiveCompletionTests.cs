using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Every place an <c>x:</c> directive is written offers what may be written there: the value directives after
/// <c>{</c>, a type and then its static members inside <c>{x:Static}</c>, and the values of the attribute directives.</summary>
[TestFixture]
public class AumlXDirectiveCompletionTests
{
    private const string Probe = """
        namespace Probe;

        public static class Metrics
        {
            public const double RailWidth = 48;
            public static double Gap { get; } = 4;
            public static readonly string Title = "Editor";
            public static void Reset() { }
        }

        public class Panelish
        {
            public static double Shared = 1;
            public double Own { get; set; }
        }

        public enum Speed { Slow, Fast }

        public static class ProbeRails
        {
            public const double Width = 2;
            public static double Gap { get; } = 6;
        }
        """;

    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe">""";

    private const string BareRoot =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">""";

    private CompletionEngine _engine;
    private string _probeFile;

    [OneTimeSetUp]
    public void BuildModel()
    {
        _probeFile = Path.Combine(Path.GetTempPath(), $"aumlprobe-{Guid.NewGuid():N}.cs");
        File.WriteAllText(_probeFile, Probe);

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        _engine = new CompletionEngine(AumlTypeModel.Build(byName.Values, [_probeFile]));
    }

    [OneTimeTearDown]
    public void RemoveProbe() => File.Delete(_probeFile);

    [TestCase("""<Border Width="{x:|}"/>""", "x:Null", "x:Static", "x:Type")]
    [TestCase("""<Border Width="{|}"/>""", "x:Null", "x:Static", "x:Type")]
    public void AfterTheBrace_TheValueDirectivesAreOffered(string body, params string[] expected) =>
        Assert.That(Labels(body), Is.SupersetOf(expected));

    [TestCase("""<Border Width="{x:Static local:|}"/>""", "Metrics", "Panelish", "Speed")]
    [TestCase("""<Border Width="{x:Static |}"/>""", "Metrics", "Panelish", "Speed")]
    [TestCase("""<Border Width="{x:Type local:|}"/>""", "Panelish", "Speed")]
    public void InATypePosition_TheTypesAreOffered(string body, params string[] expected) =>
        Assert.That(Labels(body), Is.SupersetOf(expected));

    [TestCase("""<Border Width="{x:Type local:|}"/>""")]
    [TestCase("""<Border x:ViewModel="local:|"/>""")]
    [TestCase("""<Border x:DataType="local:|"/>""")]
    public void AStaticClass_IsNoTypeToReference(string body) =>
        Assert.That(Labels(body), Does.Not.Contain("Metrics"));

    [TestCase("""<Border Width="{x:Static local:Metrics.|}"/>""", "Gap", "RailWidth", "Title")]
    [TestCase("""<Border Width="{x:Static local:Metrics.Ra|}"/>""", "RailWidth")]
    [TestCase("""<Border Width="{x:Static local:Panelish.|}"/>""", "Shared")]
    [TestCase("""<Border Width="{x:Static local:Speed.|}"/>""", "Fast", "Slow")]
    public void AfterTheTypeOfXStatic_OnlyItsStaticFieldsAndPropertiesAreOffered(string body, params string[] expected) =>
        Assert.That(Labels(body), Is.EquivalentTo(expected));

    [TestCase("""<Border x:Load="|"/>""", "False", "True")]
    [TestCase("""<Border x:Shared="|"/>""", "False", "True")]
    [TestCase("""<Border x:CreateInDesignTime="|"/>""", "False", "True")]
    [TestCase("""<Border x:KeepAlive="|"/>""", "Disabled", "Enabled", "Required")]
    public void AnAttributeDirective_OffersItsValues(string body, params string[] expected) =>
        Assert.That(Labels(body), Is.EquivalentTo(expected));

    [TestCase("""<Border x:ViewModel="local:|"/>""", "Panelish")]
    [TestCase("""<Border x:DataType="local:|"/>""", "Panelish")]
    public void ATypeDirective_OffersTypes(string body, params string[] expected) =>
        Assert.That(Labels(body), Is.SupersetOf(expected));

    [TestCase("""<Border Width="{x:Static ProbeRails.|}"/>""", "Gap", "Width")]
    [TestCase("""<Border Width="{x:Static ProbeRails.Wi|}"/>""", "Width")]
    public void AProjectTypeWithNoNamespaceDeclared_OffersItsStaticMembers(string body, params string[] expected) =>
        Assert.That(Labels(body, BareRoot), Is.EquivalentTo(expected));

    [Test]
    public void AProjectTypeWithNoNamespaceDeclared_IsOfferedBare()
    {
        var items = Items("""<Border Width="{x:Static ProbeR|}"/>""", BareRoot);

        Assert.That(items.Select(i => i.Label), Does.Contain("ProbeRails"));
        Assert.That(items.First(i => i.Label == "ProbeRails").InsertText, Is.Null, "the build finds it by name, so no prefix");
    }

    [Test]
    public void AProjectTypeWhoseShortNameTheBuildTakesForAnother_IsNotOfferedBare() =>
        Assert.That(Labels("""<Border Width="{x:Static Metr|}"/>""", BareRoot).Count(l => l == "Metrics"), Is.LessThanOrEqualTo(1),
            "the theme's Metrics is the one the build finds by that name; the probe's would be a second, unreachable one");

    private IReadOnlyList<string> Labels(string body, string root = Root) => Items(body, root).Select(i => i.Label).ToList();

    private IReadOnlyList<AumlCompletionItem> Items(string body, string root)
    {
        var marked = root + body + "</Window>";
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        return _engine.Complete(text, caret);
    }
}
