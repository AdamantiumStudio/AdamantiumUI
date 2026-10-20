using System;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="StopwatchTimer"/>: a group of its clock, its buttons and its laps, whose value is
/// the time passed in whole seconds, its hours counted past a day (<c>00:01:23</c>, <c>26:00:00</c>); read only.</summary>
public class StopwatchTimerAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    private readonly StopwatchTimer _stopwatch;

    public StopwatchTimerAutomationPeer(StopwatchTimer owner) : base(owner)
    {
        _stopwatch = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;

    public string Value => CountdownTimerAutomationPeer.Whole(_stopwatch.Elapsed);

    public bool IsReadOnly => true;

    public void SetValue(string value) => throw new InvalidOperationException($"'{AutomationId}' counts; it takes no time.");
}
