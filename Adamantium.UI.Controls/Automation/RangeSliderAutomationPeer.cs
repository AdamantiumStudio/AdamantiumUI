using System.Collections.Generic;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RangeSlider"/>: a slider whose children are its two handles, <c>Lower</c> and
/// <c>Upper</c>, each holding one bound of the span.</summary>
public class RangeSliderAutomationPeer : UIComponentAutomationPeer
{
    private RangeSliderThumbAutomationPeer _lower;
    private RangeSliderThumbAutomationPeer _upper;

    public RangeSliderAutomationPeer(RangeSlider owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Slider;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var owner = (RangeSlider)Owner;
        var track = TrackUnder(owner);
        if (track?.LowerThumb == null || track.UpperThumb == null)
        {
            return [];
        }

        if (_lower?.Owner != track.LowerThumb)
        {
            _lower = new RangeSliderThumbAutomationPeer(owner, track.LowerThumb, false);
        }

        if (_upper?.Owner != track.UpperThumb)
        {
            _upper = new RangeSliderThumbAutomationPeer(owner, track.UpperThumb, true);
        }

        return [_lower, _upper];
    }

    private static RangeTrack TrackUnder(IUIComponent element)
    {
        foreach (var child in element.VisualChildren)
        {
            if ((child as RangeTrack ?? TrackUnder(child)) is { } track)
            {
                return track;
            }
        }

        return null;
    }
}
