using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A designer edit is applied to the live tree in place. Every edit wiped the caption of every button on the
/// page: a Button's Content="Add" is a child to the live control and none to the markup, and the mismatch rebuilt the
/// button's children from nothing.</summary>
[TestFixture]
public class AumlHotReloadReconcileTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private static string Page(string caption) =>
        $"<StackPanel {Namespaces}><TextBlock Text=\"{caption}\"/><Button Content=\"Add\"/></StackPanel>";

    private static string Panel(string children) => $"<StackPanel {Namespaces}>{children}</StackPanel>";

    [TestCase("Before", TestName = "AnUnchangedPage_KeepsAButtonsCaption")]
    [TestCase("After", TestName = "AnEditElsewhere_KeepsAButtonsCaption")]
    public void AButtonsCaption_SurvivesAnEdit(string caption)
    {
        var load = AumlLoader.Load(Page("Before"));
        var page = (StackPanel)load.Root;

        var edit = AumlLoader.Reconcile(page, load, Page(caption));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(((TextBlock)page.Children[0]).Text, Is.EqualTo(caption), "the edit itself must land");
            Assert.That(((Button)page.Children[1]).Content, Is.EqualTo("Add"));
        });
    }

    [Test]
    public void AnEditedProperty_LandsOnTheSameInstances()
    {
        var load = AumlLoader.Load(Panel("<TextBlock Text=\"One\"/><Button Content=\"Run\"/>"));
        var panel = (StackPanel)load.Root;
        var text = panel.Children[0];
        var button = panel.Children[1];

        var edit = AumlLoader.Reconcile(panel, load, Panel("<TextBlock Text=\"Two\"/><Button Content=\"Run\"/>"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children[0], Is.SameAs(text));
            Assert.That(panel.Children[1], Is.SameAs(button));
            Assert.That(((TextBlock)text).Text, Is.EqualTo("Two"));
        });
    }

    [Test]
    public void AnAddedChild_IsBuiltInItsPlace_AndTheOthersStay()
    {
        var load = AumlLoader.Load(Panel("<TextBlock x:Name=\"First\"/><TextBlock x:Name=\"Last\"/>"));
        var panel = (StackPanel)load.Root;
        var first = panel.Children[0];
        var last = panel.Children[1];

        var edit = AumlLoader.Reconcile(panel, load,
            Panel("<TextBlock x:Name=\"First\"/><Button Content=\"New\"/><TextBlock x:Name=\"Last\"/>"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children.Count, Is.EqualTo(3));
            Assert.That(panel.Children[0], Is.SameAs(first));
            Assert.That(((Button)panel.Children[1]).Content, Is.EqualTo("New"));
            Assert.That(panel.Children[2], Is.SameAs(last));
        });
    }

    [Test]
    public void ARemovedChild_Leaves_AndReorderedOnesMoveAsThemselves()
    {
        var load = AumlLoader.Load(Panel("<TextBlock x:Name=\"A\"/><TextBlock x:Name=\"B\"/><TextBlock x:Name=\"C\"/>"));
        var panel = (StackPanel)load.Root;
        var a = panel.Children[0];
        var c = panel.Children[2];

        var edit = AumlLoader.Reconcile(panel, load, Panel("<TextBlock x:Name=\"C\"/><TextBlock x:Name=\"A\"/>"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children.Count, Is.EqualTo(2));
            Assert.That(panel.Children[0], Is.SameAs(c));
            Assert.That(panel.Children[1], Is.SameAs(a));
        });
    }

    [TestCase("Text=\"Plain\"", "Text=\"{Binding Source=Bound}\"", "Bound", TestName = "AValueBecomingABinding_FollowsTheBinding")]
    [TestCase("Text=\"{Binding Source=Bound}\"", "Text=\"Plain\"", "Plain", TestName = "ABindingBecomingAValue_TakesTheValue")]
    public void AnEditBetweenAValueAndABinding_Lands(string before, string after, string expected)
    {
        var load = AumlLoader.Load(Panel($"<TextBlock {before}/>"));
        var panel = (StackPanel)load.Root;
        var text = (TextBlock)panel.Children[0];

        var edit = AumlLoader.Reconcile(panel, load, Panel($"<TextBlock {after}/>"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.That(text.Text, Is.EqualTo(expected));
    }

    [Test]
    public void ARemovedProperty_GoesBackToItsDefault()
    {
        var load = AumlLoader.Load(Panel("<TextBlock Text=\"Said\"/>"));
        var panel = (StackPanel)load.Root;
        var text = (TextBlock)panel.Children[0];
        var unset = new TextBlock().Text;

        var edit = AumlLoader.Reconcile(panel, load, Panel("<TextBlock/>"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.That(text.Text, Is.EqualTo(unset));
    }

    [Test]
    public void AnEditedAttachedProperty_Lands()
    {
        var grid = $"<Grid {Namespaces} ColumnDefinitions=\"*,*\"><Button Grid.Column=\"{{0}}\"/></Grid>";
        var load = AumlLoader.Load(string.Format(grid, 0));
        var button = (Button)((Grid)load.Root).Children[0];

        var edit = AumlLoader.Reconcile(load.Root, load, string.Format(grid, 1));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.That(Grid.GetColumn(button), Is.EqualTo(1));
    }

    [Test]
    public void DefinitionsWrittenAsChildren_AreRebuilt_AndTheVisualChildStays()
    {
        const string Before = "<ColumnDefinition Width=\"*\"/><Button/>";
        const string After = "<ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"2*\"/><Button/>";
        var load = AumlLoader.Load($"<Grid {Namespaces}>{Before}</Grid>");
        var grid = (Grid)load.Root;
        var button = grid.Children[0];

        var edit = AumlLoader.Reconcile(grid, load, $"<Grid {Namespaces}>{After}</Grid>");

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(grid.ColumnDefinitions.Count, Is.EqualTo(2), "rebuilt, not added to");
            Assert.That(grid.Children.Count, Is.EqualTo(1));
            Assert.That(grid.Children[0], Is.SameAs(button));
        });
    }

    [Test]
    public void AChildTheMarkupDidNotMake_StaysPut_AndTheOthersStillTakeEdits()
    {
        const string Loaded = "<Border x:Load=\"True\" Width=\"5\"/>";
        var load = AumlLoader.Load(Panel($"<TextBlock Text=\"One\"/>{Loaded}"));
        var panel = (StackPanel)load.Root;
        var text = panel.Children[0];
        var loaded = panel.Children[1];

        var edit = AumlLoader.Reconcile(panel, load, Panel($"<TextBlock Text=\"Two\"/>{Loaded}"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children.Count, Is.EqualTo(2), string.Join(" | ", edit.Diagnostics));
            Assert.That(panel.Children[0], Is.SameAs(text), "not rebuilt");
            Assert.That(panel.Children[1], Is.SameAs(loaded), "not dropped");
            Assert.That(((TextBlock)text).Text, Is.EqualTo("Two"));
        });
    }

    [Test]
    public void AnEditedEventHandler_Replaces_AndARemovedOneLeaves()
    {
        static string Probe(string handler) =>
            $"<t:EventProbe {Namespaces} xmlns:t=\"clr-namespace:Adamantium.XamlTests;assembly=Adamantium.XamlTests\" {handler}/>";
        var load = AumlLoader.Load(Probe("Fired=\"OnA\""));
        var probe = (EventProbe)load.Root;
        Assert.That(load.Diagnostics.Where(d => !d.StartsWith("warning:")), Is.Empty);

        var swapped = AumlLoader.Reconcile(probe, load, Probe("Fired=\"OnB\""));
        probe.Fire();
        var removed = AumlLoader.Reconcile(probe, swapped, Probe(string.Empty));
        probe.Fire();

        Assert.That(removed.Reconciled, Is.True, string.Join(" | ", removed.Diagnostics));
        Assert.That(probe.Heard, Is.EqualTo((string[])["B"]));
    }

    [Test]
    public void AnEditedGradientStop_IsBuiltAgain_NotAddedToTheOld()
    {
        static string Brush(string color) =>
            $"<LinearGradientBrush {Namespaces}><GradientStop Color=\"Red\" Offset=\"0\"/><GradientStop Color=\"{color}\" Offset=\"1\"/></LinearGradientBrush>";
        var load = AumlLoader.Load(Brush("Blue"));
        var brush = (LinearGradientBrush)load.Root;

        var edit = AumlLoader.Reconcile(brush, load, Brush("Green"));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(brush.GradientStops.Count, Is.EqualTo(2));
            Assert.That(brush.GradientStops[1].Color, Is.EqualTo(Colors.Green));
        });
    }

    [Test]
    public void AnotherRootType_IsNotPatched()
    {
        var load = AumlLoader.Load(Panel("<TextBlock/>"));

        var edit = AumlLoader.Reconcile(load.Root, load, $"<Grid {Namespaces}><TextBlock/></Grid>");

        Assert.That(edit.Reconciled, Is.False);
    }
}
