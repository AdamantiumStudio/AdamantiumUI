using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Every way markup can be wrong, each failing the build with a markup error - not a C# error in the generated
/// code, not silence - at the place it is written (⟦), which is where the editor marks it too: the language server runs
/// this same build over the open file.</summary>
[TestFixture]
public class MarkupErrorCatalogTests
{
    private const char Here = '⟦';

    // A button's template with two parts, Chrome a Border and Label a TextBlock; the triggers go between it and TemplateEnd.
    private const string Template = "<Grid><ResourceContext.Resources><ControlTemplate x:Key=\"T\" TargetType=\"Button\">" +
                                    "<Border x:Name=\"Chrome\"><TextBlock x:Name=\"Label\"/></Border><ControlTemplate.Triggers>";

    private const string TemplateEnd = "</ControlTemplate.Triggers></ControlTemplate></ResourceContext.Resources></Grid>";

    [Test]
    public void ATemplatesTriggers_SettingItsPartsAsTheyAre_FailNothing()
    {
        var text = AumlCodegenHarness.WindowHeader + ">" + Template +
                   "<PropertyTrigger Property=\"IsMouseOver\" Value=\"true\"><Setter TargetName=\"Chrome\" Property=\"Background\" Value=\"Red\"/>" +
                   "<Setter TargetName=\"Label\" Property=\"Foreground\" Value=\"White\"/></PropertyTrigger>" +
                   "<PropertyTrigger SourceName=\"Chrome\" Property=\"IsMouseOver\" Value=\"true\"><Setter Property=\"Opacity\" Value=\"0.5\"/></PropertyTrigger>" +
                   "<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsMouseOver\" Value=\"true\"/><Condition Property=\"IsPressed\" Value=\"true\"/></MultiTrigger.Conditions>" +
                   "<Setter TargetName=\"Chrome\" Property=\"Opacity\" Value=\"0.8\"/></MultiTrigger>" + TemplateEnd + "</Window>";

        var errors = AumlCodegenHarness.Diagnose("MainWindow.auml", text, compile: false).Where(d => d.Severity == DiagnosticSeverity.Error);

        Assert.That(errors, Is.Empty, Said(errors));
    }

    // A whole document, for what has to be written on the root.
    private const string RootWith = "<Window x:Namespace=\"Test.App\" xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    // Reading the document.
    [TestCase("UnclosedTag", "<Grid></⟦Window>", "does not match the end tag")]
    [TestCase("UndeclaredPrefix", "<⟦foo:Bar/>", "undeclared prefix")]
    [TestCase("XmlnsOnANestedElement", "<Grid ⟦xmlns:local=\"clr-namespace:Test.App\"/>", "xmlns could be defined only at the root")]

    // Types and properties.
    [TestCase("UnknownElement", "<⟦Bogus/>", "Type Bogus could not be found")]
    [TestCase("UnknownAttribute", "<Border ⟦Bogus=\"1\"/>", "Bogus")]
    [TestCase("UnknownPropertyElement", "<Border><⟦Border.Bogus></Border.Bogus></Border>", "Bogus")]
    [TestCase("AnotherTypesPropertyElement", "<Grid><⟦PropertyGrid.Bounds></PropertyGrid.Bounds></Grid>", "not an attached property")]
    [TestCase("AnotherTypesAttribute", "<Border ⟦PropertyGrid.Bounds=\"0,0,1,1\"/>", "not an attached property")]
    [TestCase("AStaticSetterOfAnotherShape", "<Border ⟦Clipboard.Text=\"a\"/>", "not an attached property")]
    [TestCase("AnInstanceSetter", "<Border ⟦AdamantiumComponent.Value=\"a\"/>", "not an attached property")]
    [TestCase("UnknownAttachedOwner", "<Border ⟦Nope.Row=\"1\"/>", "Nope")]
    [TestCase("UnknownAttachedProperty", "<Border ⟦Grid.Nope=\"1\"/>", "Nope")]
    [TestCase("AbstractElement", "<⟦Panel/>", "Panel")]
    [TestCase("ReadOnlyProperty", "<Border ⟦ActualWidth=\"1\"/>", "ActualWidth")]

    // Values.
    [TestCase("InvalidEnum", "<Border ⟦HorizontalAlignment=\"Middle\"/>", "'Middle' is not a valid HorizontalAlignment")]
    [TestCase("InvalidNumber", "<Border ⟦Width=\"wide\"/>", "'wide' is not a valid")]
    [TestCase("InvalidBool", "<Border ⟦IsEnabled=\"yes\"/>", "'yes' is not a valid")]
    [TestCase("WrongTypeInPropertyElement", "<Border><Border.Background><⟦Grid/></Border.Background></Border>", "Grid")]

    // A property set twice.
    [TestCase("AttributeAndPropertyElement", "<Border Background=\"Red\"><⟦Border.Background>Blue</Border.Background></Border>", "Background")]
    [TestCase("PropertyElementTwice", "<Border><Border.Background>Red</Border.Background><⟦Border.Background>Blue</Border.Background></Border>", "Background")]
    [TestCase("TwoChildrenOfOneChildElement", "<Border><Grid/><⟦Grid/></Border>", "holds one child")]
    [TestCase("ContentAttributeAndChild", "<Button Content=\"Go\"><⟦TextBlock/></Button>", "Content")]
    [TestCase("TwoValuesForOneValueProperty", "<Border><Border.Child><Grid/><⟦Grid/></Border.Child></Border>", "Child")]

    // Children where there is no place for them.
    [TestCase("ChildOfAnElementWithoutContent", "<Rectangle><⟦Grid/></Rectangle>", "Rectangle")]
    [TestCase("TextInAPanel", "<Grid>⟦hello</Grid>", "Grid takes no text")]

    // Markup extensions.
    [TestCase("UnknownExtension", "<Border ⟦Tag=\"{Bogus}\"/>", "Bogus")]
    [TestCase("UnknownBindingArgument", "<Border ⟦Tag=\"{Binding Pth=X}\"/>", "Pth")]
    [TestCase("UnclosedBrace", "<Border ⟦Tag=\"{Binding X\"/>", "")]
    [TestCase("TemplateBindingOutsideATemplate", "<Border ⟦Tag=\"{TemplateBinding Width}\"/>", "TemplateBinding can only be used inside ControlTemplate")]

    // Names and keys.
    [TestCase("NameTwice", "<Grid><Border x:Name=\"A\"/><Border ⟦x:Name=\"A\"/></Grid>", "A")]
    [TestCase("NameThatIsNoIdentifier", "<Border ⟦x:Name=\"1st\"/>", "1st")]
    [TestCase("KeyTwice", "<Grid><ResourceContext.Resources><SolidColorBrush x:Key=\"A\"/><SolidColorBrush ⟦x:Key=\"A\"/></ResourceContext.Resources></Grid>", "x:Key 'A'")]

    // More of the same, written otherwise.
    [TestCase("StaticClassElement", "<⟦ToolTipService/>", "cannot be written as an element")]
    [TestCase("PropertyElementWithAttributes", "<Border><Border.Background ⟦Opacity=\"1\">Red</Border.Background></Border>", "Opacity")]
    [TestCase("PropertyElementInAPropertyElement", "<Border><Border.Child><⟦Border.Background>Red</Border.Background></Border.Child></Border>", "Background")]
    [TestCase("TemplateWithTwoRoots", "<Grid><ResourceContext.Resources><DataTemplate x:Key=\"T\"><Grid/><⟦Grid/></DataTemplate></ResourceContext.Resources></Grid>", "builds one element")]
    [TestCase("SetterOfAnUnknownProperty", "<Grid><Grid.Styles><Style Selector=\"Border\"><Setter ⟦Property=\"Bogus\" Value=\"1\"/></Style></Grid.Styles></Grid>", "Bogus")]
    [TestCase("SetterWithABadValue", "<Grid><Grid.Styles><Style Selector=\"Border\"><Setter Property=\"Width\" ⟦Value=\"wide\"/></Style></Grid.Styles></Grid>", "wide")]
    [TestCase("EventsAreNotWritten", "<Button ⟦Click=\"OnNope\"/>", "Property Click could not be found")]
    [TestCase("AttributeTwice", "<Border Width=\"1\" ⟦Width=\"2\"/>", "Width")]
    [TestCase("EmptyNumber", "<Border ⟦Width=\"\"/>", "is not a valid")]
    [TestCase("RootWithoutXmlns", "<⟦Window x:Namespace=\"Test.App\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"><Grid/></Window>", "Xmlns declaration is missing")]
    [TestCase("UnknownXmlns",RootWith + " xmlns:n=\"http://nowhere/controls\"><⟦n:Thing/></Window>", "http://nowhere/controls")]
    [TestCase("UnknownClrNamespace", RootWith + " xmlns:n=\"clr-namespace:Gone.Away\"><⟦n:Thing/></Window>", "Thing")]

    // Triggers and the parts of a template they set.
    [TestCase("TargetNameOfNoPart", Template + "<PropertyTrigger Property=\"IsMouseOver\" Value=\"true\"><Setter ⟦TargetName=\"Nope\" Property=\"Background\" Value=\"Red\"/></PropertyTrigger>" + TemplateEnd, "has no part named 'Nope'")]
    [TestCase("APartsPropertyThatIsNot", Template + "<PropertyTrigger Property=\"IsMouseOver\" Value=\"true\"><Setter TargetName=\"Label\" ⟦Property=\"BorderThickness\" Value=\"1\"/></PropertyTrigger>" + TemplateEnd, "Property BorderThickness could not be found in TextBlock")]
    [TestCase("APartsBadValue", Template + "<PropertyTrigger Property=\"IsMouseOver\" Value=\"true\"><Setter TargetName=\"Chrome\" Property=\"Width\" ⟦Value=\"wide\"/></PropertyTrigger>" + TemplateEnd, "'wide' is not a valid")]
    [TestCase("TriggerOfAPropertyThatIsNot", Template + "<PropertyTrigger ⟦Property=\"IsHoverd\" Value=\"true\"/>" + TemplateEnd, "Property IsHoverd could not be found in Button")]
    [TestCase("TriggerWithABadValue", Template + "<PropertyTrigger Property=\"IsMouseOver\" ⟦Value=\"maybe\"/>" + TemplateEnd, "'maybe' is not a valid")]
    [TestCase("SourceNameOfNoPart", Template + "<PropertyTrigger ⟦SourceName=\"Nope\" Property=\"IsMouseOver\" Value=\"true\"/>" + TemplateEnd, "has no part named 'Nope'")]
    [TestCase("ConditionOfAPropertyThatIsNot", Template + "<MultiTrigger><MultiTrigger.Conditions><Condition ⟦Property=\"IsHoverd\" Value=\"true\"/></MultiTrigger.Conditions></MultiTrigger>" + TemplateEnd, "IsHoverd")]
    [TestCase("StyleTriggerOfNoPart", "<Grid><Grid.Styles><Style Selector=\"Button\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\"><Border x:Name=\"Chrome\"/></ControlTemplate></Setter.Value></Setter><PropertyTrigger Property=\"IsMouseOver\" Value=\"true\"><Setter ⟦TargetName=\"Nope\" Property=\"Background\" Value=\"Red\"/></PropertyTrigger></Style></Grid.Styles></Grid>", "has no part named 'Nope'")]
    [TestCase("ElementTriggerOfAPropertyThatIsNot", "<Border><Border.Triggers><DataTrigger Binding=\"{Binding X}\" Value=\"true\"><Setter ⟦Property=\"Bogus\" Value=\"1\"/></DataTrigger></Border.Triggers></Border>", "Property Bogus could not be found in Border")]

    // Directives.
    [TestCase("UnknownDirective", "<Border ⟦x:Nmae=\"A\"/>", "Unknown directive 'x:Nmae'")]
    [TestCase("StaticMemberThatIsNot", "<Border ⟦Width=\"{x:Static Grid.Nope}\"/>", "has no member 'Nope'")]
    [TestCase("TypeThatIsNot", "<Border ⟦Tag=\"{x:Type Nope}\"/>", "Nope")]
    public void TheBuildFailsWithAMarkupError_WhereItIsWritten(string name, string body, string expected)
    {
        var marked = body.Replace(Here.ToString(), string.Empty).StartsWith("<Window", StringComparison.Ordinal)
            ? body
            : AumlCodegenHarness.WindowHeader + ">" + body + "</Window>";
        var at = marked.IndexOf(Here);
        var text = marked.Remove(at, 1);
        var (line, column) = LineColumn(text, at);

        var diagnostics = AumlCodegenHarness.Diagnose("MainWindow.auml", text, compile: true);
        var flagged = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("CS", StringComparison.Ordinal)).ToList();

        Assert.That(flagged.Any(d => d.GetMessage().Contains(expected, StringComparison.Ordinal) && IsAt(d, line, column)), Is.True,
            $"{name}: expected a markup error containing '{expected}' at {line + 1}:{column + 1}; the build said:\n{Said(diagnostics)}");
    }

    private static bool IsAt(Diagnostic diagnostic, int line, int column)
    {
        var start = diagnostic.Location.GetLineSpan().StartLinePosition;
        return diagnostic.Location != Location.None && start.Line == line && start.Character == column;
    }

    private static string Said(IEnumerable<Diagnostic> diagnostics) => string.Join("\n", diagnostics
        .Where(d => d.Severity >= DiagnosticSeverity.Warning)
        .Select(d =>
        {
            var start = d.Location.GetLineSpan().StartLinePosition;
            var where = d.Location == Location.None ? "nowhere" : $"{start.Line + 1}:{start.Character + 1}";
            return $"  {d.Id} {where} {d.GetMessage()}";
        }));

    private static (int Line, int Column) LineColumn(string text, int offset)
    {
        var before = text[..offset];
        var line = before.Count(c => c == '\n');
        return (line, offset - (before.LastIndexOf('\n') + 1));
    }
}
