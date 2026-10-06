using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A template selector written in markup: templates each stating the type they draw, picked by the item's type -
/// its own first, then its base classes, then an interface - and one with no type for whatever no other fits. Inline or
/// from resources, built the same way by the build and by the preview.</summary>
[TestFixture]
public class DataTemplateSetTests
{
    private const string Set = """
        <DataTemplateSet>
            <DataTemplate x:DataType="{x:Type Brush}"><TextBlock Text="any brush"/></DataTemplate>
            <DataTemplate x:DataType="{x:Type SolidColorBrush}"><TextBlock Text="a solid brush"/></DataTemplate>
            <DataTemplate><TextBlock Text="anything else"/></DataTemplate>
        </DataTemplateSet>
        """;

    public interface IShape
    {
    }

    public class Shape
    {
    }

    public class Circle : Shape, IShape
    {
    }

    public class Square : IShape
    {
    }

    [Test]
    public void ASetInPlaceOfASelector_Builds_AndTellsEachTemplateItsType()
    {
        var markup = AumlCodegenHarness.WindowHeader
                     + $"><ListBox><ListBox.ItemTemplateSelector>{Set}</ListBox.ItemTemplateSelector></ListBox></Window>";

        var generated = AumlCodegenHarness.Generate(markup, out var generatorErrors);
        var compileErrors = AumlCodegenHarness.Compile(markup);

        Assert.That(generatorErrors.Concat(compileErrors).Select(e => e.GetMessage()), Is.Empty);
        Assert.That(generated, Does.Contain("DataType = typeof(global::Adamantium.UI.Core.Media.SolidColorBrush)"));
    }

    [Test]
    public void ASetFromResources_IsTakenByKey()
    {
        var markup = AumlCodegenHarness.WindowHeader + $"""
            ><ResourceContext.Resources>{Set.Replace("<DataTemplateSet>", "<DataTemplateSet x:Key=\"Brushes\">")}</ResourceContext.Resources>
            <ListBox ItemTemplateSelector="{"{ResourceReference Brushes}"}"/></Window>
            """;

        AumlCodegenHarness.Generate(markup, out var generatorErrors);
        var compileErrors = AumlCodegenHarness.Compile(markup);

        Assert.That(generatorErrors.Concat(compileErrors).Select(e => e.GetMessage()), Is.Empty);
    }

    [TestCase("""<DataTemplate x:DataType="{x:Type Brush}"><Border/></DataTemplate><DataTemplate x:DataType="{x:Type Brush}"><Border/></DataTemplate>""", "two templates for Brush")]
    [TestCase("""<DataTemplate><Border/></DataTemplate><DataTemplate><Border/></DataTemplate>""", "more than one template without x:DataType")]
    public void ASetThatCannotChoose_FailsTheBuild(string templates, string expected)
    {
        var markup = AumlCodegenHarness.WindowHeader
                     + $"><ListBox><ListBox.ItemTemplateSelector><DataTemplateSet>{templates}</DataTemplateSet></ListBox.ItemTemplateSelector></ListBox></Window>";

        AumlCodegenHarness.Generate(markup, out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains(expected));
    }

    [Test]
    public void ThePreview_PicksTheNearestType()
    {
        var result = AumlLoader.Load(
            $"""<ListBox xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions"><ListBox.ItemTemplateSelector>{Set}</ListBox.ItemTemplateSelector></ListBox>""");
        var set = (result.Root as ListBox)?.ItemTemplateSelector as DataTemplateSet;

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        Assert.That(set?.Templates, Has.Count.EqualTo(3));
        Assert.That(set.SelectTemplate(new SolidColorBrush(), null).DataType, Is.EqualTo(typeof(SolidColorBrush)));
        Assert.That(set.SelectTemplate(new LinearGradientBrush(), null).DataType, Is.EqualTo(typeof(Brush)));
        Assert.That(set.SelectTemplate("text", null).DataType, Is.Null);
    }

    [Test]
    public void TheOwnTypeWins_ThenABaseClass_ThenAnInterface_ThenTheOneWithoutAType()
    {
        var forShape = new DataTemplate { DataType = typeof(Shape) };
        var forInterface = new DataTemplate { DataType = typeof(IShape) };
        var forCircle = new DataTemplate { DataType = typeof(Circle) };
        var fallback = new DataTemplate();
        var set = new DataTemplateSet { Templates = { forInterface, forShape, fallback } };

        Assert.That(set.SelectTemplate(new Circle(), null), Is.SameAs(forShape), "a base class before an interface");
        Assert.That(set.SelectTemplate(new Square(), null), Is.SameAs(forInterface));
        Assert.That(set.SelectTemplate(42, null), Is.SameAs(fallback));
        Assert.That(set.SelectTemplate(null, null), Is.SameAs(fallback));

        set.Templates.Add(forCircle);

        Assert.That(set.SelectTemplate(new Circle(), null), Is.SameAs(forCircle), "its own type, once there is one");
    }
}
