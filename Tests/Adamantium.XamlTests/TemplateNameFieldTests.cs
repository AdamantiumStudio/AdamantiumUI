using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A name given inside a template belongs to each copy the template stamps, not to the view: it is found through
/// the template, and the view gets no field for it. Such a field was declared and never set - a warning, which a build
/// with warnings as errors refused.</summary>
[TestFixture]
public class TemplateNameFieldTests
{
    private const string View =
        "<View xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"><StackPanel>" +
        "<TextBlock x:Name=\"Title\" Text=\"Rows\"/>" +
        "<ItemsControl><ItemsControl.ItemTemplate><DataTemplate>" +
        "<StackPanel><TextBlock x:Name=\"RowLabel\" Text=\"Delay\"/>" +
        "<Slider AutomationProperties.LabeledBy=\"{Binding ElementName=RowLabel}\"/></StackPanel>" +
        "</DataTemplate></ItemsControl.ItemTemplate></ItemsControl>" +
        "</StackPanel></View>";

    [Test]
    public void ANameInsideATemplate_GetsNoFieldOfTheView_AndTheViewCompilesWithoutWarnings()
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string> { ["Views/Rows.auml"] = View });

        var warnings = project.Compilation.GetDiagnostics().Where(d => d.Id is "CS0169" or "CS0649").Select(d => d.GetMessage());
        Assert.That(warnings, Is.Empty);
        Assert.That(project.Source, Does.Contain(" Title;").And.Not.Contain(" RowLabel;"));
    }
}
