using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
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

    [TestCase("Before", TestName = "AnUnchangedPage_KeepsAButtonsCaption")]
    [TestCase("After", TestName = "AnEditElsewhere_KeepsAButtonsCaption")]
    public void AButtonsCaption_SurvivesAnEdit(string caption)
    {
        var load = AumlLoader.Load(Page("Before"));
        var page = (StackPanel)load.Root;

        var edit = AumlLoader.Reconcile(page, load.Ast, Page(caption));

        Assert.That(edit.Reconciled, Is.True, string.Join(" | ", edit.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(((TextBlock)page.Children[0]).Text, Is.EqualTo(caption), "the edit itself must land");
            Assert.That(((Button)page.Children[1]).Content, Is.EqualTo("Add"));
        });
    }
}
