namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Whether a finger lays ink with a tool that inks. A finger that does not ink moves the plane instead; two
/// fingers zoom it either way.</summary>
public enum CanvasFingerInk
{
    /// <summary>Fingers ink, a pen beside them or not - several people at one large screen.</summary>
    Draw,

    /// <summary>Fingers ink until a pen comes within reach of the screen, then only move the plane - a hand resting on
    /// the screen while it writes leaves no marks.</summary>
    WhenNoPen,

    /// <summary>Fingers never ink.</summary>
    Never
}
