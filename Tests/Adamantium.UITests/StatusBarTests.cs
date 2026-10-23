using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Automation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A status bar's fields tell, and a field with a command or a menu acts on a press: it runs the command, or
/// opens the menu; items that are not fields are shown in fields of their own.</summary>
[TestFixture]
public class StatusBarTests
{
    [Test]
    public void AFieldWithACommandOrAMenu_IsInteractive_AndOneWithNeither_OnlyTells()
    {
        var field = new StatusBarItem { Content = "Ready" };
        var passive = field.IsInteractive;

        field.Command = new AdamantiumCommand(() => { });
        var commanded = field.IsInteractive;

        field.Command = null;
        field.ContextMenu = new ContextMenu();
        var withMenu = field.IsInteractive;

        Assert.Multiple(() =>
        {
            Assert.That(passive, Is.False);
            Assert.That(commanded, Is.True);
            Assert.That(withMenu, Is.True);
        });
    }

    [Test]
    public void APress_RunsTheFieldsCommand()
    {
        var ran = 0;
        var field = new StatusBarItem { Content = "Ln 1, Col 1", Command = new AdamantiumCommand(() => ran++) };

        field.PerformClick();

        Assert.That(ran, Is.EqualTo(1));
    }

    [Test]
    public void ToAutomation_AFieldThatActs_IsAButton_AndOneThatTells_IsText()
    {
        var telling = new StatusBarItem { Content = "Ready" };
        var acting = new StatusBarItem { Content = "100%", Command = new AdamantiumCommand(() => { }) };

        Assert.Multiple(() =>
        {
            Assert.That(telling.GetAutomationPeer().ControlType, Is.EqualTo(AutomationControlType.Text));
            Assert.That(telling.GetAutomationPeer().GetPattern(PatternId.Invoke), Is.Null);
            Assert.That(acting.GetAutomationPeer().ControlType, Is.EqualTo(AutomationControlType.Button));
            Assert.That(acting.GetAutomationPeer().GetPattern(PatternId.Invoke), Is.Not.Null);
            Assert.That(new StatusBar().GetAutomationPeer().ControlType, Is.EqualTo(AutomationControlType.StatusBar));
        });
    }

    [Test]
    public void ADataItem_IsShownInAFieldOfItsOwn_AndAnElement_StandsAsItIs()
    {
        var bar = new StatusBar();
        var field = new StatusBarItem { Content = "UTF-8" };
        var progress = new ProgressBar();
        bar.Items.Add("CRLF");
        bar.Items.Add(field);
        bar.Items.Add(progress);

        var made = bar.ItemContainerGenerator.Realize(0);

        Assert.Multiple(() =>
        {
            Assert.That(made, Is.TypeOf<StatusBarItem>());
            Assert.That(((StatusBarItem)made).Content, Is.EqualTo("CRLF"));
            Assert.That(bar.ItemContainerGenerator.Realize(1), Is.SameAs(field));
            Assert.That(bar.ItemContainerGenerator.Realize(2), Is.SameAs(progress));
        });
    }

    [Test]
    public void APress_OpensTheFieldsMenu_AndTheNextPress_ClosesIt_EvenAfterARightClickOpenedItAtAPoint()
    {
        var menu = new ContextMenu();
        var field = new StatusBarItem { Content = "CRLF", ContextMenu = menu };
        menu.Placement = PlacementMode.Relative;
        menu.HorizontalOffset = 30;

        field.PerformClick();
        var opened = menu.IsOpen;
        var placement = menu.Placement;
        field.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(menu.IgnoreTargetPress, Is.True, "the field's own press must not dismiss it first");
            Assert.That(opened, Is.True);
            Assert.That(placement, Is.Not.EqualTo(PlacementMode.Relative), "over the field, not at the old point");
            Assert.That(menu.HorizontalOffset, Is.Zero);
            Assert.That(menu.IsOpen, Is.False);
        });
    }

    [Test]
    public void AFieldThatOnlyTells_DoesNotClick()
    {
        var clicked = 0;
        var field = new StatusBarItem { Content = "Ready" };
        field.Click += (_, _) => clicked++;

        field.PerformClick();

        Assert.That(clicked, Is.Zero);
    }
}
