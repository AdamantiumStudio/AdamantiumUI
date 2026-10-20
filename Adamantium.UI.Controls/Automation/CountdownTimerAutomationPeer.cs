using System;
using System.Globalization;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CountdownTimer"/>: a group of its clock and its buttons, whose value is what is left
/// in whole seconds, its hours counted past a day (<c>00:04:59</c>, <c>26:00:00</c>); read only - its length is its
/// <see cref="CountdownTimer.Duration"/>.</summary>
public class CountdownTimerAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    private readonly CountdownTimer _timer;

    public CountdownTimerAutomationPeer(CountdownTimer owner) : base(owner)
    {
        _timer = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;

    public string Value => Whole(_timer.Remaining);

    public bool IsReadOnly => true;

    public void SetValue(string value) =>
        throw new InvalidOperationException($"'{AutomationId}' counts down; its length is its Duration.");

    internal static string Whole(TimeSpan length) =>
        ((long)length.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" +
        length.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
        length.Seconds.ToString("00", CultureInfo.InvariantCulture);
}
