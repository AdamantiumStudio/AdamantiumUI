using System.Linq;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A resource marker written with no key - <c>{ResourceReference }</c> mid-edit - fails the build and the
/// preview with a message naming it, where it used to crash the generator with an index out of range.</summary>
[TestFixture]
public class ResourceKeyMissingTests
{
    [TestCase("ResourceReference")]
    [TestCase("ObservableResource")]
    [TestCase("ThemeResource")]
    public void AMarkerWithNoKey_FailsTheBuildByName(string marker)
    {
        AumlCodegenHarness.Generate(
            AumlCodegenHarness.WindowHeader + $"><Border Background=\"{{{marker} }}\"/></Window>", out var errors);

        var messages = errors.Select(e => e.GetMessage()).ToList();
        Assert.That(messages, Has.Some.Contains($"{{{marker}}} on Background names no key"), string.Join(" | ", messages));
        Assert.That(messages, Has.None.Contains("AUML generation failed"), "a missing key is a mistake to report, not a crash");
    }

    [Test]
    public void AMarkerWithNoKey_IsReportedByThePreviewToo()
    {
        var result = AumlLoader.Load(
            "<Border xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\" Background=\"{ObservableResource }\"/>");

        Assert.That(result.Diagnostics, Has.Some.Contains("{ObservableResource} on Background names no key"),
            string.Join(" | ", result.Diagnostics));
    }
}
