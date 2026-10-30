namespace Adamantium.UI.Controls.Panels;

/// <summary>How a <see cref="RenderTargetPanel"/> is handed the frames rendered for it.</summary>
public enum FrameSharing
{
    /// <summary>Within the logical device when the renderer is on the UI's own - its image and timelines as they are,
    /// nothing through the system; through OS handles when it is on another.</summary>
    Auto,

    /// <summary>Always through OS handles, as across processes and devices - to try that path where the other is taken.
    /// </summary>
    ThroughHandles
}
