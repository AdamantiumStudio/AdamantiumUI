using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A markup extension whose type is not found is reported as such. The generator crashed on it, reading the
/// arguments of a type it never resolved.</summary>
[TestFixture]
public class UnresolvedMarkupExtensionTests
{
    [TestCase("{Zz_NoSuch}")]
    [TestCase("{Zz_NoSuch Value}")]
    [TestCase("{Zz_NoSuch Path=Value}")]
    [TestCase("{Binding Value, Converter={Zz_NoSuch}}")]
    [TestCase("{Binding Value, Converter={x:Zz_NoSuch}}")]
    [TestCase("{Binding Value, Converter={conv:Zz_NoSuch}}")]
    [TestCase("{conv:Zz_NoSuch}")]
    public void AnExtensionOfNoType_IsReported_NotCrashedOn(string value)
    {
        var markup = AumlCodegenHarness.WindowHeader
            + $" xmlns:conv=\"clr-namespace:Zz.NoSuch\"><TextBlock Text=\"{value}\"/></Window>";
        AumlCodegenHarness.Generate(markup, out var errors);

        Assert.That(errors.Select(e => e.Id), Has.None.EqualTo("AUI900"), string.Join(" | ", errors.Select(e => e.GetMessage())));
        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("Zz_NoSuch"));
    }
}
