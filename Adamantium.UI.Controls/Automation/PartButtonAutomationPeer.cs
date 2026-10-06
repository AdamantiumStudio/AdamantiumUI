using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>A plain part of a template that its control answers presses on - the × of a chip, the funnel of a column
/// header: a button, called by its tooltip, that does what the press does.</summary>
public class PartButtonAutomationPeer : UIComponentAutomationPeer, IInvokeProvider
{
    private readonly Action _press;

    internal PartButtonAutomationPeer(UIComponent owner, Action press) : base(owner)
    {
        _press = press;
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    public void Invoke() => _press();

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
