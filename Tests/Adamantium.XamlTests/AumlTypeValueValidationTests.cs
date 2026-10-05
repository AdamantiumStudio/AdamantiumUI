using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The editor underlines a type given to a <see cref="Type"/>-valued property that the build would reject -
/// one it would not find, or one not derived from the property's <c>[TypeOf]</c> base - with the build's own message, in
/// every form the type is written in.</summary>
[TestFixture]
public class AumlTypeValueValidationTests
{
    private const string Probe = """
        namespace Probe;

        public class ProbeBrushes : Adamantium.UI.Core.Resources.ResourceDictionary { }

        public class ProbeButtons : Adamantium.UI.Core.Resources.StyleSet { }
        """;

    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe">""";

    private const string NotAStyleSet =
        "ProbeBrushes is not a StyleSet: StyleInclude.Source takes a type derived from Adamantium.UI.Core.Resources.StyleSet";

    private const string NotADictionary =
        "ProbeButtons is not a ResourceDictionary: ResourceLink.Source takes a type derived from Adamantium.UI.Core.Resources.ResourceDictionary";

    private AumlTypeModel _model;
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

        _model = AumlTypeModel.Build(byName.Values, [_probeFile]);
    }

    [OneTimeTearDown]
    public void RemoveProbe() => File.Delete(_probeFile);

    [TestCase("""<StyleInclude Source="ProbeButtons"/>""")]
    [TestCase("""<StyleInclude Source="{x:Type local:ProbeButtons}"/>""")]
    [TestCase("""<Border ResourceContext.Source="{ResourceLink Source=ProbeBrushes}"/>""")]
    [TestCase("""<Border ResourceContext.Source="{ResourceLink Source={x:Type local:ProbeBrushes}, Scope=Global}"/>""")]
    [TestCase("""<DropDown EnumType="HorizontalAlignment"/>""")]
    [TestCase("""<Border Tag="ProbeBrushes"/>""")]
    public void WhatTheBuildTakesIsNotFlagged(string body) =>
        Assert.That(BuildErrors(body), Is.Empty);

    [TestCase("""<StyleInclude Source="ProbeBrushes"/>""", "ProbeBrushes", NotAStyleSet)]
    [TestCase("""<StyleInclude Source="{x:Type local:ProbeBrushes}"/>""", "local:ProbeBrushes", NotAStyleSet)]
    [TestCase("""<Border ResourceContext.Source="{ResourceLink Source=ProbeButtons}"/>""", "ProbeButtons", NotADictionary)]
    [TestCase("""<Border ResourceContext.Source="{ResourceLink Scope=Global, Source={x:Type ProbeButtons}}"/>""", "ProbeButtons", NotADictionary)]
    [TestCase("""<StyleInclude Source="NoSuchStyles"/>""", "NoSuchStyles", "Type NoSuchStyles could not be found in any linked assembly")]
    [TestCase("""<DropDown EnumType="local:Missing"/>""", "local:Missing", "Type Missing could not be found in namespace Probe")]
    [TestCase("""<StyleInclude Source="{x:Type local:Missing}"/>""", "local:Missing", "Type Missing could not be found in namespace Probe")]
    [TestCase("""<Border Tag="{x:Type NoSuchStyles}"/>""", "NoSuchStyles", "Type NoSuchStyles could not be found in any linked assembly")]
    public void WhatTheBuildRejectsIsFlaggedWhereItIsWritten(string body, string written, string message)
    {
        var errors = BuildErrors(body);

        Assert.That(errors.Select(e => e.Message), Is.EqualTo(new[] { message }));
        Assert.That(errors[0].Character, Is.EqualTo((Root + body).IndexOf(written, (Root + body).IndexOf('"', Root.Length), StringComparison.Ordinal)));
        Assert.That(errors[0].Length, Is.EqualTo(written.Length));
    }

    private List<AumlDiagnostic> BuildErrors(string body) =>
        AumlValidator.Validate(Root + body + "</Window>", _model).Where(d => d.Code == "AUM001").ToList();
}
