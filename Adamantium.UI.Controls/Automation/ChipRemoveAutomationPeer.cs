using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The × of a chip, drawn as a plain part its strip answers presses on: a button, called by its tooltip, that
/// takes the chip's key out.</summary>
public class ChipRemoveAutomationPeer : UIComponentAutomationPeer, IInvokeProvider
{
    private readonly Action _remove;

    internal ChipRemoveAutomationPeer(UIComponent owner, Action remove) : base(owner)
    {
        _remove = remove;
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    public void Invoke() => _remove();

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
