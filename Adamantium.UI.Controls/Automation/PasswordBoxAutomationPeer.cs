using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="PasswordBox"/>: an edit field that says it holds a password and never tells what
/// it is. A value can be set, so a test can type one in.</summary>
public class PasswordBoxAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    public PasswordBoxAutomationPeer(PasswordBox owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Edit;

    public override bool IsPassword => true;

    public string Value => string.Empty;

    public bool IsReadOnly => ((PasswordBox)Owner).IsReadOnly;

    /// <summary>Writes the password as a current value, so a binding on it keeps following.</summary>
    public void SetValue(string value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{AutomationId}' is read-only.");
        }

        Owner.SetCurrentValue(PasswordBox.PasswordProperty, value);
    }

    /// <summary>Its placeholder, when nothing else names it.</summary>
    protected override string NameCore() => ((PasswordBox)Owner).Placeholder is { Length: > 0 } placeholder ? placeholder : null;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
