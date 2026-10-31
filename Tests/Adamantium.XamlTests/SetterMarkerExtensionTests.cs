using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>{Ancestor} and {Self} as a setter's value, in a style written in a view as well as in a dictionary, are kept
/// for the setter to bind on each element it styles, as {Binding} is; in a view's own style they were applied to the
/// setter itself, which is no element, and the view did not compile.</summary>
[TestFixture]
public class SetterMarkerExtensionTests
{
    [TestCase("{Ancestor ListBox, DataContext}")]
    [TestCase("{Self Width}")]
    public void AMarkerInAViewsOwnStyle_Compiles(string value)
    {
        var auml = AumlCodegenHarness.WindowHeader + "><ListBox><ListBox.ItemContainerStyle>"
                   + "<Style Selector=\"ListBoxItem\"><Setter Property=\"Tag\" Value=\"" + value + "\"/></Style>"
                   + "</ListBox.ItemContainerStyle></ListBox></Window>";

        var errors = AumlCodegenHarness.Compile(auml);

        Assert.That(errors.Select(error => error.GetMessage()), Is.Empty);
    }
}
