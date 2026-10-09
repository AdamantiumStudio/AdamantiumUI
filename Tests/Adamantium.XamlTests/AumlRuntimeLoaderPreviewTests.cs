using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// What the designer shows is built by the runtime loader, not by the generated code, so it has to build the same tree
/// from the same markup. Each case here previewed wrong or not at all: one value it could not apply took the whole
/// document down, and the designer then called a perfectly visual control "not previewable".
/// </summary>
[TestFixture]
public class AumlRuntimeLoaderPreviewTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    [Test]
    public void ALiveResourceMarker_ConnectsItself_InsteadOfBeingAssigned()
    {
        var result = AumlLoader.Load($"<Border {Namespaces} Background=\"{{ObservableResource Accent}}\"/>");

        Assert.That(result.Root, Is.InstanceOf<Border>(), string.Join(" | ", result.Diagnostics));
        Assert.That(result.Diagnostics.Any(d => d.Contains("cannot be converted")), Is.False, string.Join(" | ", result.Diagnostics));
    }

    [Test]
    public void AnAncestorBinding_TakesItsTypeAndPath_FromThePositionalArguments()
    {
        var result = AumlLoader.Load(
            $"<StackPanel {Namespaces}><Button Command=\"{{Ancestor StackPanel, DataContext.Run}}\"/></StackPanel>");

        Assert.That(result.Root, Is.InstanceOf<StackPanel>(), string.Join(" | ", result.Diagnostics));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void AValueTheLoaderCannotApply_IsReported_AndTheRestStillBuilds()
    {
        var result = AumlLoader.Load(
            $"<StackPanel {Namespaces}><Border Width=\"wide\"/><Border/></StackPanel>");

        Assert.That(result.Root, Is.InstanceOf<StackPanel>());
        Assert.That(((StackPanel)result.Root).Children.Count, Is.EqualTo(2));
        Assert.That(result.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void AnAttachedProperty_IsSetThroughItsOwner()
    {
        var result = AumlLoader.Load($"<Grid {Namespaces}><Border Grid.Row=\"1\" Grid.Column=\"2\"/></Grid>");

        var border = (Border)((Grid)result.Root).Children[0];
        Assert.That(Grid.GetRow(border), Is.EqualTo(1));
        Assert.That(Grid.GetColumn(border), Is.EqualTo(2));
    }

    [Test]
    public void AnElementName_IsCarried_AndABindingMayUseIt()
    {
        var result = AumlLoader.Load(
            $"<StackPanel {Namespaces}><TextBlock x:Name=\"Source\" Text=\"a\"/>" +
            "<TextBlock Text=\"{Binding Text, ElementName=Source}\"/></StackPanel>");

        var source = (TextBlock)((StackPanel)result.Root).Children[0];
        Assert.That(source.Name, Is.EqualTo("Source"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void InlineResources_AreKeyedIntoADictionary()
    {
        // A View root, as the views that carry their own resources are: the transformer resolves every type in it.
        var result = AumlLoader.Load(
            $"<View x:Namespace=\"Test.App\" {Namespaces}><ResourceContext.Resources>" +
            "<SolidColorBrush x:Key=\"Accent\" Color=\"Red\"/><SolidColorBrush x:Key=\"Ink\" Color=\"Blue\"/>" +
            "</ResourceContext.Resources></View>");

        var resources = ResourceContext.GetResources((Adamantium.UI.Core.AdamantiumComponent)result.Root);
        Assert.That(resources, Is.Not.Null, string.Join(" | ", result.Diagnostics));
        Assert.That(resources["Accent"], Is.InstanceOf<SolidColorBrush>());
        Assert.That(resources["Ink"], Is.InstanceOf<SolidColorBrush>());
    }

    [Test]
    public void ADataTemplate_BuildsItsContent_WithItsNamedParts()
    {
        var result = AumlLoader.Load(
            $"<ContentControl {Namespaces}><ContentControl.ContentTemplate><DataTemplate>" +
            "<TextBlock x:Name=\"Label\" Text=\"x\"/></DataTemplate></ContentControl.ContentTemplate></ContentControl>");

        var template = ((ContentControl)result.Root).ContentTemplate;
        Assert.That(template, Is.Not.Null, string.Join(" | ", result.Diagnostics));
        var built = template.Build(null);
        Assert.That(built.RootComponent, Is.InstanceOf<TextBlock>());
        Assert.That(built.GetComponentByName("Label"), Is.SameAs(built.RootComponent));
        Assert.That(template.Build(null).RootComponent, Is.Not.SameAs(built.RootComponent), "every build makes its own copy");
    }

    [Test]
    public void AControlTemplate_FollowsTheControl_ThroughItsTemplateBindings()
    {
        var result = AumlLoader.Load(
            $"<ControlTemplate {Namespaces} TargetType=\"Button\"><Border Background=\"{{TemplateBinding Background}}\"/></ControlTemplate>");

        var template = (ControlTemplate)result.Root;
        Assert.That(template.TargetType, Is.EqualTo(typeof(Button)), string.Join(" | ", result.Diagnostics));
        var button = new Button { Background = Brushes.Green };
        var border = (Border)template.Build(button).RootComponent;
        Assert.That(border.Background, Is.SameAs(Brushes.Green));
    }

    [Test]
    public void AControlTemplate_FollowsTheControl_OnAnAttachedProperty()
    {
        var result = AumlLoader.Load(
            $"<ControlTemplate {Namespaces} TargetType=\"Button\"><Border ToolTipService.ToolTip=\"{{TemplateBinding Content}}\"/></ControlTemplate>");

        var template = (ControlTemplate)result.Root;
        var border = (Border)template.Build(new Button { Content = "Run" }).RootComponent;
        Assert.That(ToolTipService.GetToolTip(border), Is.EqualTo("Run"), string.Join(" | ", result.Diagnostics));
    }

    [Test]
    public void AGenericCollectionProperty_IsFilled_NotAssigned()
    {
        var result = AumlLoader.Load(
            $"<Ribbon {Namespaces}><Ribbon.ContextualGroups><RibbonContextualGroup/></Ribbon.ContextualGroups></Ribbon>");

        Assert.That(((Ribbon)result.Root).ContextualGroups.Count, Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
    }
}
