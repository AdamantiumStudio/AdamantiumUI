using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// <c>x:Key="{x:Static Type.Member}"</c>: a resource keyed by a constant code uses too - an entity's icon key - so the key
/// is written once. It was read as text only, and an entry keyed this way was left out of its dictionary in silence.
/// </summary>
[TestFixture]
public class AumlStaticResourceKeyTests
{
    // A real public string constant: ApplicationData.FolderVariable.
    private const string Key = "{x:Static ui:ApplicationData.FolderVariable}";

    private const string DictionaryHeader =
        "<ResourceDictionary x:Namespace=\"Test.App\" xmlns=\"http://adamantium/ui\" " +
        "xmlns:x=\"http://adamantium/ui/xaml/extensions\" xmlns:ui=\"clr-namespace:Adamantium.UI;assembly=Adamantium.UI\">";

    private const string Read = "global::Adamantium.UI.ApplicationData.FolderVariable";

    [Test]
    public void AnEntryKeyedByAStaticIsAddedUnderIt()
    {
        var code = AumlCodegenHarness.Generate(
            DictionaryHeader + $"<ContextMenu x:Key=\"{Key}\"/></ResourceDictionary>", out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain($"Add({Read}"), "the entry is added under the member's value\n" + code);
    }

    // The assembly names the key by the constant's value, so another project's markup finds it by that text.
    [Test]
    public void TheAssemblyNamesAStaticKeyByItsValue()
    {
        var code = AumlCodegenHarness.Generate(
            DictionaryHeader + $"<ContextMenu x:Key=\"{Key}\"/></ResourceDictionary>", out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain("ResourceKeyAttribute(\"ADAMANTIUM_APP_DATA\""), code);
    }

    [Test]
    public void ASharedFalseEntryKeyedByAStaticIsAddedUnderIt()
    {
        var code = AumlCodegenHarness.Generate(
            DictionaryHeader + $"<ContextMenu x:Key=\"{Key}\" x:Shared=\"False\"/></ResourceDictionary>", out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain($"Add({Read}"), "the factory is added under the member's value\n" + code);
    }

    [Test]
    public void AnInlineEntryKeyedByAStaticIsAddedUnderIt()
    {
        var code = AumlCodegenHarness.Generate(
            AumlCodegenHarness.WindowHeader.Replace("x:Class=", "xmlns:ui=\"clr-namespace:Adamantium.UI;assembly=Adamantium.UI\" x:Class=") +
            $"><Grid><ResourceContext.Resources><ContextMenu x:Key=\"{Key}\"/></ResourceContext.Resources></Grid></Window>",
            out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain($"Add({Read}"), "the inline entry is added under the member's value\n" + code);
    }

    [Test]
    public void TheGeneratedDictionaryCompiles()
    {
        var errors = AumlCodegenHarness.Compile(
            DictionaryHeader + $"<ContextMenu x:Key=\"{Key}\"/><ContextMenu x:Key=\"Plain\"/></ResourceDictionary>");

        Assert.That(errors, Is.Empty, "generated code did not compile: " + string.Join(" | ", errors.Select(d => d.ToString())));
    }

    // A dictionary is keyed by text: a member of another type is said in markup, not left to fail in generated code.
    [Test]
    public void AStaticThatIsNotTextIsAnError()
    {
        AumlCodegenHarness.Generate(
            DictionaryHeader.Replace("xmlns:ui=", "xmlns:panels=\"clr-namespace:Adamantium.UI.Controls.Panels;assembly=Adamantium.UI.Controls\" xmlns:ui=") +
            "<ContextMenu x:Key=\"{x:Static panels:VirtualizingPanel.MinBindsPerPassDefault}\"/></ResourceDictionary>",
            out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("MinBindsPerPassDefault"),
            AumlCodegenHarness.Errors(errors));
    }

    // "{}" still says the braces that follow are the key itself.
    [Test]
    public void AnEscapedBraceKeyStaysText()
    {
        var code = AumlCodegenHarness.Generate(
            DictionaryHeader + "<ContextMenu x:Key=\"{}{Menu}\"/></ResourceDictionary>", out var errors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(code, Does.Contain("Add(\"{}{Menu}\""), code);
    }

    // Anything else is said, not dropped: an entry with a key the dictionary cannot hold is a build error.
    [Test]
    public void AKeyThatIsNeitherTextNorAStaticIsAnError()
    {
        AumlCodegenHarness.Generate(
            DictionaryHeader + "<ContextMenu x:Key=\"{x:Type ContextMenu}\"/></ResourceDictionary>", out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("x:Key"), AumlCodegenHarness.Errors(errors));
    }
}
