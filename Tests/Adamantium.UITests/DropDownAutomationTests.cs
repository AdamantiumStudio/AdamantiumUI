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

        new ListBoxItemAutomationPeer((DropDownItem)drop.Items[1]).Select();

        Assert.Multiple(() =>
        {
            Assert.That(drop.SelectedIndex, Is.EqualTo(1));
            Assert.That(command.Ran, Is.EqualTo(new object[] { "C" }));
        });
    }
}
