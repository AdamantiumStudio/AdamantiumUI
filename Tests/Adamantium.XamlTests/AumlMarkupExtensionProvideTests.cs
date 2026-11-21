using System;
using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.MarkupExtensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An application's own markup extension gives the property it is written on what its ProvideObject gives,
/// told the element and the property - built by the generator and by the designer's loader alike.</summary>
[TestFixture]
public class AumlMarkupExtensionProvideTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\" " +
        "xmlns:t=\"clr-namespace:Adamantium.XamlTests;assembly=Adamantium.XamlTests\"";

    private const string Markup =
        "<StackPanel " + Namespaces + "><TextBlock x:Name=\"Told\" Text=\"{t:WhereWritten}\" Width=\"{t:Twice Value=21}\"/>" +
        "<TextBlock x:Name=\"Placed\" Grid.Row=\"{t:Row Index=3}\"/></StackPanel>";

    // On an element, and on a part of a template: a property and an attached one each.
    [TestCase("<StackPanel>{0}</StackPanel>")]
    [TestCase("<ContentControl><ContentControl.ContentTemplate><DataTemplate>{0}</DataTemplate></ContentControl.ContentTemplate></ContentControl>")]
    public void TheGeneratedCode_Compiles(string around)
    {
        var errors = AumlCodegenHarness.Compile(AumlCodegenHarness.WindowHeader + " xmlns:t=\"clr-namespace:Adamantium.XamlTests;assembly=Adamantium.XamlTests\">" +
            string.Format(around, "<TextBlock Text=\"{t:WhereWritten}\" Width=\"{t:Twice Value=21}\" Grid.Row=\"{t:Row Index=3}\"/>") +
            "</Window>");

        Assert.That(errors, Is.Empty, string.Join(" | ", errors.Select(d => d.ToString())));
    }

    [Test]
    public void TheProperty_TakesWhatProvideObjectGives_ToldTheElementAndTheProperty()
    {
        var result = AumlLoader.Load(Markup);
        var panel = (StackPanel)result.Root;
        Assert.That(panel, Is.Not.Null, string.Join(" | ", result.Diagnostics));
        var told = (TextBlock)panel.Children[0];
        var placed = (TextBlock)panel.Children[1];

        Assert.Multiple(() =>
        {
            Assert.That(told.Text, Is.EqualTo("TextBlock.Text"));
            Assert.That(told.Width, Is.EqualTo(42));
            Assert.That(Grid.GetRow(placed), Is.EqualTo(3), "an attached property takes it too");
        });
    }

    [Test]
    public void InATemplate_TheProperty_TakesIt_ForEachBuild()
    {
        var result = AumlLoader.Load(
            "<ContentControl " + Namespaces + "><ContentControl.ContentTemplate><DataTemplate>" +
            "<TextBlock Text=\"{t:WhereWritten}\" Width=\"{t:Twice Value=5}\" Grid.Row=\"{t:Row Index=2}\"/>" +
            "</DataTemplate></ContentControl.ContentTemplate></ContentControl>");
        var template = ((ContentControl)result.Root)?.ContentTemplate;
        Assert.That(template, Is.Not.Null, string.Join(" | ", result.Diagnostics));

        var built = (TextBlock)template.Build(null).RootComponent;

        Assert.Multiple(() =>
        {
            Assert.That(built.Text, Is.EqualTo("TextBlock.Text"));
            Assert.That(built.Width, Is.EqualTo(10));
            Assert.That(Grid.GetRow(built), Is.EqualTo(2));
            Assert.That(built.IsSet(Adamantium.UI.Controls.Base.MeasurableUIComponent.WidthProperty, Adamantium.UI.Core.ValuePriority.Template),
                Is.True, "at the template's priority, so its triggers outrank it");
            Assert.That(built.IsSet(Grid.RowProperty, Adamantium.UI.Core.ValuePriority.Template), Is.True, "an attached property too");
        });
    }
}

/// <summary>Twice its <see cref="Value"/>.</summary>
public sealed class Twice : MarkupExtension
{
    public double Value { get; set; }

    public override object ProvideObject(MarkupContext context) => Value * 2;
}

/// <summary>The kind of element and the property it is written on.</summary>
public sealed class WhereWritten : MarkupExtension
{
    public override object ProvideObject(MarkupContext context) =>
        $"{context.TargetObject?.GetType().Name}.{context.TargetPropertyName}";
}

/// <summary>A grid row by its index.</summary>
public sealed class Row : MarkupExtension
{
    public int Index { get; set; }

    public override object ProvideObject(MarkupContext context) => Index;
}
