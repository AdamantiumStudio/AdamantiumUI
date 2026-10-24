namespace Adamantium.UI.EntityServices;

/// <summary>An overlay stage that records its frame where the tree is laid out, on the loop thread, and only applies it
/// on the thread that draws.</summary>
internal interface IRecordingStage
{
    void Record();

    /// <summary>A frame it recorded waits to be drawn.</summary>
    bool HasRecordedFrame { get; }

    /// <summary>After its pre-render: a picture it shows waits for a frame submitted elsewhere.</summary>
    bool AwaitsFrame { get; }
}
