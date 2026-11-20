using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A drop-down's rows chosen through automation, as a person's click chooses them.</summary>
[TestFixture]
public class DropDownAutomationTests
{
    private sealed class Recorded : Adamantium.Core.Commands.ICommand
    {
        public readonly List<object> Ran = [];

        public event System.EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter = null) => true;

        public void Execute(object parameter = null) => Ran.Add(parameter);

        public void RaiseCanExecuteChanged()
        {
        }
    }

    // A row given in markup, chosen before the list was ever opened: no container is generated for it yet.
    [Test]
    public void ARowOfMarkup_ChosenBeforeTheListOpened_IsSelected_AndRunsTheCommand()
    {
        var command = new Recorded();
        var drop = new DropDown { SelectedValuePath = "Tag", Command = command };
        drop.Items.Add(new DropDownItem { Content = "Left", Tag = "L" });
        drop.Items.Add(new DropDownItem { Content = "Center", Tag = "C" });

        var center = new ListBoxItemAutomationPeer((DropDownItem)drop.Items[1]);

        center.Select();

        Assert.Multiple(() =>
        {
            Assert.That(drop.SelectedIndex, Is.EqualTo(1));
            Assert.That(command.Ran, Is.EqualTo(new object[] { "C" }));
            Assert.That(center.IsSelected, Is.True, "the row says it is chosen");
            Assert.That(((DropDownItem)drop.Items[0]).IsSelected, Is.False);
        });
    }

    // A drop-down that is off takes no row, chosen by automation or not.
    [Test]
    public void AnOffDropDown_TakesNoRowOfMarkup()
    {
        var drop = new DropDown { IsEnabled = false };
        drop.Items.Add(new DropDownItem { Content = "Left" });
        drop.Items.Add(new DropDownItem { Content = "Center" });

        new ListBoxItemAutomationPeer((DropDownItem)drop.Items[1]).Select();

        Assert.That(drop.SelectedIndex, Is.EqualTo(-1));
    }
}
