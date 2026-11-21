using System;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A converter is a markup extension written <c>{Name}</c>, with no resource declared: made once for the place it
/// is written and kept for every build of that place, its properties set from the markup. A type that is no markup
/// extension is not written in braces.</summary>
[TestFixture]
public class AumlConverterExtensionTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\" " +
        "xmlns:t=\"clr-namespace:Adamantium.XamlTests;assembly=Adamantium.XamlTests\"";

    [Test]
    public void AConverter_IsKeptForItsPlace_ByTheGeneratedClass_AndCompiles()
    {
        var markup = AumlCodegenHarness.WindowHeader +
                     "><TextBlock Text=\"x\" Visibility=\"{Binding Shown, Converter={BooleanToVisibilityConverter}}\"/></Window>";

        var code = AumlCodegenHarness.Generate(markup, out var errors);
        var compiled = AumlCodegenHarness.Compile(markup);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
            Assert.That(code, Does.Match(@"private static global::Adamantium\.UI\.Controls\.Converters\.BooleanToVisibilityConverter _Shared_converter_\d+;"));
            Assert.That(code, Does.Not.Contain("Converter = new global::Adamantium.UI.Controls.Converters.BooleanToVisibilityConverter()"));
            Assert.That(compiled, Is.Empty, string.Join(" | ", compiled.Select(d => d.ToString())));
        });
    }

    [Test]
    public void AConverterInATemplate_IsOneForEveryBuild_AndOneForEachPlace()
    {
        var template = TemplateOf(
            "<StackPanel><TextBlock x:Name=\"First\" Width=\"{Binding Size, Converter={t:ScaleConverter Factor=2}}\"/>" +
            "<TextBlock x:Name=\"Second\" Width=\"{Binding Size, Converter={t:ScaleConverter Factor=3}}\"/></StackPanel>");

        var once = template.Build(null);
        var again = template.Build(null);

        var first = ConverterOf(once.GetComponentByName("First"));
        Assert.Multiple(() =>
        {
            Assert.That(ConverterOf(again.GetComponentByName("First")), Is.SameAs(first), "a build makes no converter of its own");
            Assert.That(ConverterOf(once.GetComponentByName("Second")), Is.Not.SameAs(first), "each place has its own");
            Assert.That(((ScaleConverter)first).Factor, Is.EqualTo(2));
            Assert.That(((ScaleConverter)ConverterOf(once.GetComponentByName("Second"))).Factor, Is.EqualTo(3));
        });
    }

    [Test]
    public void AMultiBindingsConverter_IsKeptForItsPlace_Too()
    {
        var template = TemplateOf(
            "<TextBlock x:Name=\"Sum\"><TextBlock.Width><MultiBinding Converter=\"{t:SumConverter}\">" +
            "<Binding Path=\"A\"/><Binding Path=\"B\"/></MultiBinding></TextBlock.Width></TextBlock>");

        var once = template.Build(null);
        var again = template.Build(null);

        var converter = MultiConverterOf(once.GetComponentByName("Sum"));
        Assert.Multiple(() =>
        {
            Assert.That(converter, Is.InstanceOf<SumConverter>());
            Assert.That(MultiConverterOf(again.GetComponentByName("Sum")), Is.SameAs(converter));
        });
    }

    // A static property can change - a language's current table - so it is read at each build, as before.
    [Test]
    public void AConverterGivenAStaticMember_IsMadePerBuild_WithItsValueThen()
    {
        var template = TemplateOf(
            "<TextBlock x:Name=\"Scaled\" Width=\"{Binding Size, Converter={t:ScaleConverter Factor={x:Static t:Factors.Current}}}\"/>");

        Factors.Current = 2;
        var once = (ScaleConverter)ConverterOf(template.Build(null).GetComponentByName("Scaled"));
        Factors.Current = 3;
        var again = (ScaleConverter)ConverterOf(template.Build(null).GetComponentByName("Scaled"));

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.Not.SameAs(once));
            Assert.That(once.Factor, Is.EqualTo(2));
            Assert.That(again.Factor, Is.EqualTo(3));
        });
    }

    [Test]
    public void AConverterGivenAConverterOfConstants_IsKeptForItsPlace_AndSoIsTheOneGiven()
    {
        var template = TemplateOf(
            "<TextBlock x:Name=\"Scaled\" Width=\"{Binding Size, Converter={t:ScaleConverter Then={t:ScaleConverter Factor=2}}}\"/>");

        var once = (ScaleConverter)ConverterOf(template.Build(null).GetComponentByName("Scaled"));
        var again = (ScaleConverter)ConverterOf(template.Build(null).GetComponentByName("Scaled"));

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.SameAs(once));
            Assert.That(((ScaleConverter)once.Then).Factor, Is.EqualTo(2));
        });
    }

    [Test]
    public void AConverterGivenAnotherExtension_IsMadePerBuild()
    {
        var template = TemplateOf(
            "<TextBlock x:Name=\"Scaled\" Width=\"{Binding Size, Converter={t:ScaleConverter Then={t:ScaleConverter Factor={x:Static t:Factors.Current}}}}\"/>");

        var once = (ScaleConverter)ConverterOf(template.Build(null).GetComponentByName("Scaled"));
        var again = (ScaleConverter)ConverterOf(template.Build(null).GetComponentByName("Scaled"));

        Assert.That(again, Is.Not.SameAs(once));
    }

    [TestCase("{Binding Shown, Converter={Border}}")]
    [TestCase("{Border}")]
    public void ATypeThatIsNoMarkupExtension_IsNotWrittenInBraces(string value)
    {
        var markup = AumlCodegenHarness.WindowHeader + $"><TextBlock Text=\"{value}\"/></Window>";

        AumlCodegenHarness.Generate(markup, out var errors);

        Assert.That(errors.Select(e => e.GetMessage()), Has.Some.Contains("is not a markup extension"),
            string.Join(" | ", errors.Select(e => e.GetMessage())));
    }

    private static Adamantium.UI.Core.Templates.DataTemplate TemplateOf(string content)
    {
        var result = AumlLoader.Load(
            $"<ContentControl {Namespaces}><ContentControl.ContentTemplate><DataTemplate>{content}</DataTemplate>" +
            "</ContentControl.ContentTemplate></ContentControl>");
        var template = ((ContentControl)result.Root)?.ContentTemplate as Adamantium.UI.Core.Templates.DataTemplate;
        Assert.That(template, Is.Not.Null, string.Join(" | ", result.Diagnostics));
        Assert.That(result.Diagnostics.Where(d => !d.StartsWith("warning:", StringComparison.Ordinal)), Is.Empty);
        return template;
    }

    private static IValueConverter ConverterOf(IAdamantiumComponent element) =>
        ((Binding)BindingEngine.GetBindingExpression(element, MeasurableUIComponent.WidthProperty).BindingBase).Converter;

    private static IMultiValueConverter MultiConverterOf(IAdamantiumComponent element) =>
        ((MultiBinding)BindingEngine.GetBindingExpression(element, MeasurableUIComponent.WidthProperty).BindingBase).Converter;
}

/// <summary>A number times <see cref="Factor"/>, then through <see cref="Then"/>.</summary>
public sealed class ScaleConverter : ValueConverter
{
    public double Factor { get; set; } = 1;

    public IValueConverter Then { get; set; }

    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double number
            ? Then?.Convert(number * Factor, targetType, parameter, culture) ?? number * Factor
            : AdamantiumProperty.UnsetValue;

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double number ? number / Factor : AdamantiumProperty.UnsetValue;
}

/// <summary>The sum of the numbers.</summary>
public sealed class SumConverter : MultiValueConverter
{
    public override object Convert(object[] value, Type targetType, object parameter, CultureInfo culture) =>
        value.OfType<double>().Sum();

    public override object[] ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}

/// <summary>A factor markup reads as a static member, which can change.</summary>
public static class Factors
{
    public static double Current { get; set; } = 1;
}
