using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The editor flags a type in <c>{x:Type}</c> and a type or member in <c>{x:Static}</c> that the build would
/// not find - found the way the build finds it - and paints such a type as unknown.</summary>
[TestFixture]
public class AumlTypeReferenceValidationTests
{
    private const string Probe = """
        namespace Probe;

        public static class ProbeRails
        {
            public static double Gap { get; } = 4;
        }
        """;

    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe">""";

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

    [TestCase("""<Border Width="{x:Static local:ProbeRails.Gap}"/>""")]
    [TestCase("""<Border Width="{x:Static ProbeRails.Gap}"/>""")]
    [TestCase("""<Border Tag="{x:Type local:ProbeRails}"/>""")]
    [TestCase("""<Border Tag="{x:Type Border}"/>""")]
    [TestCase("""<Border Tag="{x:Type nobody:Metrics}"/>""")]
    [TestCase("""<!-- <Border Tag="{x:Type local:Missing}"/> --><Border/>""")]
    public void WhatTheBuildFindsIsNotFlagged(string body) =>
        Assert.That(BuildErrors(body), Is.Empty);

    [TestCase("""<Border Tag="{x:Type local:Missing}"/>""", "local:Missing", "Type Missing could not be found in namespace Probe")]
    [TestCase("""<Border Width="{x:Static local:ProbeRails.Nope}"/>""", "local:ProbeRails.Nope", "x:Static: 'Probe.ProbeRails' has no member 'Nope'")]
    [TestCase("""<Border Width="{x:Static local:Nope.Gap}"/>""", "local:Nope.Gap", "x:Static type 'local:Nope' could not be resolved")]
    [TestCase("""<Border Width="{x:Static ProbeRails.}"/>""", "ProbeRails.", "x:Static expects 'Type.Member', got 'ProbeRails.'")]
    public void WhatTheBuildWouldNotFindIsFlaggedWhereItIsWritten(string body, string written, string message)
    {
        var errors = BuildErrors(body);

        Assert.That(errors.Select(e => e.Message), Is.EqualTo(new[] { message }));
        Assert.That(errors[0].Character, Is.EqualTo((Root + body).IndexOf(written, StringComparison.Ordinal)));
        Assert.That(errors[0].Length, Is.EqualTo(written.Length));
    }

    [TestCase("""<Border Tag="{x:Type local:Missing}"/>""", "Missing", SemanticTokensEngine.Unknown)]
    [TestCase("""<Border Tag="{x:Type local:ProbeRails}"/>""", "ProbeRails", SemanticTokensEngine.Type)]
    [TestCase("""<Border Width="{x:Static ProbeRails.Gap}"/>""", "ProbeRails", SemanticTokensEngine.Type)]
    [TestCase("""<Border Width="{x:Static ProbeRails.Gap}"/>""", "Gap", SemanticTokensEngine.Property)]
    public void TheTypeIsPaintedByWhetherItExists(string body, string word, int expected)
    {
        var text = Root + body + "</Window>";
        var start = text.IndexOf(word, text.IndexOf('{'), StringComparison.Ordinal);

        var token = SemanticTokensEngine.Tokenize(text, _model).Single(t => t.Start == start);

        Assert.That(token.TokenType, Is.EqualTo(expected));
        Assert.That(token.Length, Is.EqualTo(word.Length));
    }

    [TestCase("http://adamantium/ui", "Thickness")]
    [TestCase("clr-namespace:System;assembly=System.Runtime", "Double")]
    public void AnElementTheBuildFinds_IsKnown(string xmlns, string name) =>
        Assert.That(_model.GetElement(xmlns, name), Is.Not.Null);

    private List<AumlDiagnostic> BuildErrors(string body) =>
        AumlValidator.Validate(Root + body + "</Window>", _model).Where(d => d.Code == "AUM001").ToList();
}
