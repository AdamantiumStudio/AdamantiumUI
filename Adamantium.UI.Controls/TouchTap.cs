using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls;

/// <summary>A finger's press on a row, waiting to be a tap. One that moves past the drag threshold - scrolling the list,
/// carrying the row - is no tap, so a row a finger scrolls past is not selected. A row selects on the tap, or as a hold
/// starts on it.</summary>
internal sealed class TouchTap
{
    private Pointer _pointer;
    private Vector2 _from;
    private IInputComponent _measuredIn;

    /// <summary>Starts waiting when the press is a finger's; false for the mouse and the pen, which act on the press.</summary>
    public bool Begin(PointerPressedEventArgs e, IInputComponent measuredIn)
    {
        if (e.Pointer.Type != PointerType.Touch)
        {
            return false;
        }

        _pointer = e.Pointer;
        _measuredIn = measuredIn;
        _from = e.GetPosition(measuredIn);
        return true;
    }

    public void Move(PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _pointer) &&
            PlatformSettings.ExceedsDragThreshold(e.GetPosition(_measuredIn) - _from, _measuredIn))
        {
            _pointer = null;
        }
    }

    /// <summary>Stops waiting: what the press was on is gone, a row reused for other data, say.</summary>
    public void Cancel() => _pointer = null;

    /// <summary>Whether this release, or this hold as it starts, ends the waiting press as a tap.</summary>
    public bool End(PointerEventArgs e)
    {
        var tap = ReferenceEquals(e.Pointer, _pointer);
        if (tap)
        {
            _pointer = null;
        }

        return tap;
    }
}
