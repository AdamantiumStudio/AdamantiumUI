using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Owner.Name on an element of another type is an attached property, set through the owner's static SetName.
/// A plain property of another type has none - <c>&lt;Grid&gt;&lt;PropertyGrid.Bounds&gt;</c> - and fails the build,
/// and the editor says so where it is written.</summary>
[TestFixture]
public class AttachedPropertyOwnerTests
{
    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">""";

    private AumlTypeModel _model;

    [OneTimeSetUp]
    public void BuildModel()
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

        _model = AumlTypeModel.Build(byName.Values);
    }

    [TestCase("<Grid><PropertyGrid.Bounds></PropertyGrid.Bounds></Grid>", "PropertyGrid.Bounds")]
    [TestCase("<Grid><Border PropertyGrid.Bounds=\"0,0,1,1\"/></Grid>", "PropertyGrid.Bounds")]
    public void AnotherTypesPlainProperty_FailsTheBuild_AndIsFlaggedWhereItIsWritten(string body, string written)
    {
        const string message = "PropertyGrid.Bounds is not an attached property: it is set on a PropertyGrid only, not on";

        AumlCodegenHarness.Generate(AumlCodegenHarness.WindowHeader + ">" + body + "</Window>", out var errors);
        var text = Root + body + "</Window>";
        var flagged = AumlValidator.Validate(text, _model).Where(d => d.Message.StartsWith(message, StringComparison.Ordinal)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains(message));
            Assert.That(flagged, Has.Count.EqualTo(1));
            Assert.That(flagged.Select(d => d.Length), Has.All.EqualTo(written.Length));
        });
    }

    [TestCase("<Grid><Border Grid.Row=\"1\"/></Grid>")]
    [TestCase("<Border><ResourceContext.Resources></ResourceContext.Resources></Border>")]
    [TestCase("<Grid><Grid.RowDefinitions><RowDefinition/></Grid.RowDefinitions></Grid>")]
    public void AnAttachedProperty_AndTheElementsOwnProperty_AreNot(string body)
    {
        AumlCodegenHarness.Generate(AumlCodegenHarness.WindowHeader + ">" + body + "</Window>", out var errors);
        var flagged = AumlValidator.Validate(Root + body + "</Window>", _model)
            .Where(d => d.Message.Contains("not an attached property", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(errors.Select(e => e.GetMessage()), Has.None.Contains("not an attached property"));
            Assert.That(flagged, Is.Empty);
        });
    }
}
