using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TextBox"/>: an edit field whose value is its text.</summary>
public class TextBoxAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    public TextBoxAutomationPeer(TextBox owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Edit;

    public string Value => ((TextBox)Owner).Text ?? string.Empty;

    public bool IsReadOnly => ((TextBox)Owner).IsReadOnly;

    /// <summary>Writes the text as a current value, so a binding on it keeps following.</summary>
    public void SetValue(string value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{AutomationId}' is read-only.");
        }

        Owner.SetCurrentValue(TextBox.TextProperty, value);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
