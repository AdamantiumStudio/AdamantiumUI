using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.MarkupExtensions;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A <see cref="System.Type"/>-valued property takes a type by name or by <c>{x:Type}</c>, and one marked
/// <c>[TypeOf]</c> only a type derived from its base: the build takes a dictionary file for a <c>ResourceLink</c> and a
/// style set file for a <c>StyleInclude</c>, and fails on any other type instead of leaving it to throw when the window
/// opens - and so does the live preview.</summary>
[TestFixture]
public class TypeOfValueTests
{
    private const string Namespaces = "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private static GeneratedProject Build(string linked, string extension, string included) => AumlCodegenHarness.Project(
        new Dictionary<string, string>
        {
            ["AppColors.auml"] = $"<ResourceDictionary {Namespaces}/>",
            ["Buttons.auml"] = $"<StyleSet {Namespaces}/>",
            ["MainWindow.auml"] =
                $"<Window {Namespaces}>" +
                $"<ResourceContext.Resources><ResourceLink Source=\"{linked}\"/></ResourceContext.Resources>" +
                $"<Grid ResourceContext.Source=\"{{ResourceLink Source={extension}}}\"/>" +
                "</Window>",
            ["AppTheme.auml"] =
                $"<Theme {Namespaces}>" +
                $"<Theme.StyleIncludes><StyleInclude Source=\"{included}\"/></Theme.StyleIncludes>" +
                "</Theme>",
        });

    [TestCase("AppColors", "{x:Type AppColors}", "Buttons")]
    [TestCase("{x:Type AppColors}", "AppColors", "{x:Type Buttons}")]
    public void ATypeOfTheBase_IsTaken(string linked, string extension, string included)
    {
        var project = Build(linked, extension, included);

        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));
    }

    [TestCase("Buttons", "{x:Type AppColors}", "Buttons", "Buttons is not a ResourceDictionary")]
    [TestCase("{x:Type Border}", "{x:Type AppColors}", "Buttons", "Border is not a ResourceDictionary")]
    [TestCase("AppColors", "{x:Type Buttons}", "Buttons", "Buttons is not a ResourceDictionary")]
    [TestCase("AppColors", "Buttons", "Buttons", "Buttons is not a ResourceDictionary")]
    [TestCase("AppColors", "{x:Type AppColors}", "AppColors", "AppColors is not a StyleSet")]
    [TestCase("AppColors", "{x:Type AppColors}", "{x:Type Border}", "Border is not a StyleSet")]
    public void ATypeOfAnotherKind_FailsTheBuild(string linked, string extension, string included, string message)
    {
        var errors = Build(linked, extension, included).GeneratorDiagnostics.Select(d => d.GetMessage()).ToList();

        Assert.That(errors, Has.Some.Contains(message), string.Join(" | ", errors));
    }

    [Test]
    public void AnExtensionArgument_TakesATypeByName()
    {
        var source = Build("AppColors", "AppColors", "Buttons").Source;

        Assert.That(source, Does.Not.Contain("Parse<global::System.Type>"), "a type name read as text is no type at all");
        Assert.That(source.Split(".Source = typeof(global::Test.App.AppColors)").Length - 1, Is.EqualTo(2), source);
    }

    [TestCase("Border")]
    [TestCase("{x:Type Border}")]
    public void AnAncestor_TakesItsTypeByNameOrByXType(string type)
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string>
        {
            ["MainWindow.auml"] = $"<Window {Namespaces}><Border><TextBlock Width=\"{{Ancestor {type}, Width, Stop={type}}}\"/></Border></Window>",
        });

        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));
        Assert.That(project.Source, Does.Contain("AncestorType = typeof(global::Adamantium.UI.Controls.Decorators.Border)"));
        Assert.That(project.Source, Does.Contain("Stop = typeof(global::Adamantium.UI.Controls.Decorators.Border)"));
    }

    [Test]
    public void ATypePropertyWithNoBase_TakesATypeByName()
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string>
        {
            ["MainWindow.auml"] = $"<Window {Namespaces}><DropDown EnumType=\"HorizontalAlignment\"/></Window>",
        });

        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));
        Assert.That(project.Source, Does.Contain("EnumType = typeof(global::Adamantium.UI.Core.HorizontalAlignment)"));
    }

    [TestCase("ResourceDictionary")]
    [TestCase("{x:Type ResourceDictionary}")]
    public void ThePreviewTakesATypeOfTheBaseByName(string linked)
    {
        var result = AumlLoader.Load($"<ResourceLink {Namespaces} Source=\"{linked}\"/>");

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        Assert.That(((ResourceLink)result.Root).Source, Is.EqualTo(typeof(ResourceDictionary)));
    }

    [TestCase("Border")]
    [TestCase("{x:Type Border}")]
    public void ThePreviewRejectsATypeOfAnotherKindToo(string linked)
    {
        var result = AumlLoader.Load($"<ResourceLink {Namespaces} Source=\"{linked}\"/>");

        Assert.That(result.Diagnostics, Has.Some.Contains("Border is not a ResourceDictionary"), string.Join(" | ", result.Diagnostics));
    }
}
