using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls;

/// <summary>A press on a row waiting to be a tap: a finger's always - one that scrolls the list past the row selects
/// nothing - and a mouse's or a pen's when asked, a press on one of several rows chosen, which a drag carries together.
/// One that moves past the drag threshold, or is let go away from where it was pressed, is no tap. A row selects on the
/// tap, or as a hold starts on it.</summary>
internal sealed class RowTap
{
    private Pointer _pointer;
    private Vector2<double> _from;
    private IInputComponent _measuredIn;

    /// <summary>The keys held as the waiting press came: what its tap is read with, not the keys held at the release.
    /// </summary>
    public InputModifiers Modifiers { get; private set; }

    /// <summary>Starts waiting when the press is a finger's, or <paramref name="anyPointer"/>; false for a mouse or pen
    /// press that acts at once.</summary>
    public bool Begin(PointerPressedEventArgs e, IInputComponent measuredIn, bool anyPointer = false)
    {
        if (e.Pointer.Type != PointerType.Touch && !anyPointer)
        {
            _pointer = null;
            return false;
        }

        _pointer = e.Pointer;
        _measuredIn = measuredIn;
        _from = e.GetPosition(measuredIn);
        Modifiers = e.Modifiers;
        return true;
    }

    public void Move(PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _pointer) && Moved(e))
        {
            _pointer = null;
        }
    }

    /// <summary>Stops waiting: what the press was on is gone, a row reused for other data, say.</summary>
    public void Cancel() => _pointer = null;

    /// <summary>Whether this release, or this hold as it starts, ends the waiting press as a tap.</summary>
    public bool End(PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer))
        {
            return false;
        }

        _pointer = null;
        // Let go away from the press: a drag ended there, whose moves a drag source may have taken.
        return !Moved(e);
    }

    private bool Moved(PointerEventArgs e) =>
        PlatformSettings.ExceedsDragThreshold(e.GetPosition(_measuredIn) - _from, _measuredIn);
}
