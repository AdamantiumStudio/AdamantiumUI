using NUnit.Framework;

namespace Adamantium.XamlTests;

// Generated code sits in the project's namespace, and a name it writes is looked up there first: in Studio.Adamantium
// "Adamantium.UI.Controls.Window" meant Studio.Adamantium.UI and did not compile.
[TestFixture]
public class AumlCodegenNamespaceTests
{
    [Test]
    public void AProjectNamespaceEndingInAdamantiumDoesNotCaptureTheFrameworkNames()
    {
        const string auml =
            "<Window x:Namespace=\"Studio.Adamantium\" " +
            "xmlns=\"http://adamantium/ui\" " +
            "xmlns:x=\"http://adamantium/ui/xaml/extensions\">" +
            "<Grid>" +
            "<TextBlock Grid.Row=\"1\" Text=\"{Binding Title}\" HorizontalAlignment=\"Center\" Margin=\"0,16,0,0\"/>" +
            "</Grid>" +
            "</Window>";

        var errors = AumlCodegenHarness.Compile(auml);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }
}
