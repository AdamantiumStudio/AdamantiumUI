using System;
using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Clock"/>: text whose value is the time it shows, as its language writes it; read
/// only.</summary>
public class ClockAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    private readonly Clock _clock;

    public ClockAutomationPeer(Clock owner) : base(owner)
    {
        _clock = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Text;

    public string Value => _clock.ShownText;

    public bool IsReadOnly => true;

    public void SetValue(string value) => throw new InvalidOperationException($"'{AutomationId}' shows a time; it takes none.");

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
