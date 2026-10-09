using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Font features are a string in markup, checked against the OpenType registry by the build and by the editor,
/// with the same message.</summary>
[TestFixture]
public class FontFeaturesMarkupTests
{
    private const string Header =
        "<Window xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">";

    private const string Misspelled = "'lgia' is not a registered OpenType feature. Did you mean 'liga'?";

    private static string WindowWith(string body) => Header + body + "</Window>";

    [Test]
    public void RegisteredFeaturesAndTypography_Compile()
    {
        var errors = AumlCodegenHarness.Compile(WindowWith(
            "<TextBlock Text=\"office\" FontFeatures=\"liga=0, ss01, cv05=2\" Typography.Capitals=\"SmallCaps\" " +
            "Typography.Ligatures=\"Standard|Discretionary\"/>"));

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void WeightSlantAndWidth_Compile()
    {
        var errors = AumlCodegenHarness.Compile(WindowWith(
            "<TextBlock FontWeight=\"SemiBold\" FontStyle=\"Italic\" FontStretch=\"Condensed\">" +
            "<TextBlock.Inlines><Run Text=\"heavy\" FontWeight=\"900\" FontStyle=\"Oblique\"/></TextBlock.Inlines>" +
            "</TextBlock>"));

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void AxisValues_Compile()
    {
        var errors = AumlCodegenHarness.Compile(WindowWith(
            "<TextBlock FontVariations=\"wght=650, GRAD=150, opsz=auto\">" +
            "<TextBlock.Inlines><Run Text=\"large\" FontVariations=\"opsz=144\"/></TextBlock.Inlines>" +
            "</TextBlock>"));

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void AMisspelledWeight_FailsTheBuild()
    {
        AumlCodegenHarness.Generate(WindowWith("<TextBlock FontWeight=\"Semibld\"/>"), out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("'Semibld' is not a font weight"));
    }

    [Test]
    public void AMisspelledFeature_FailsTheBuild()
    {
        AumlCodegenHarness.Generate(WindowWith("<TextBlock Text=\"office\" FontFeatures=\"lgia=0\"/>"), out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains(Misspelled));
    }

    [Test]
    public void AMisspelledFeature_IsUnderlinedInTheEditor()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        var model = AumlTypeModel.Build(byName.Values, []);

        var diagnostics = AumlValidator.Validate(WindowWith("<TextBlock FontFeatures=\"lgia=0\"/>"), model);

        Assert.That(diagnostics.Select(d => d.Message), Has.Some.Contains(Misspelled));
    }
}
