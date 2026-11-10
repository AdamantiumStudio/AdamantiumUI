namespace Adamantium.UI.Rendering;

/// <summary>How the walk puts a group of draws down this frame.</summary>
internal enum CommandGroupMode
{
    /// <summary>It changes nothing: its draws are drawn as they come.</summary>
    Pass,

    /// <summary>It only cuts to a rectangle lying along the window's axes: its draws are drawn cut by a scissor.</summary>
    Scissor,

    /// <summary>Its draws go into a target of its own, its masks into theirs, and it is laid down at its end.</summary>
    Layer,

    /// <summary>Nothing of it shows: none of it is drawn.</summary>
    Skipped,
}
