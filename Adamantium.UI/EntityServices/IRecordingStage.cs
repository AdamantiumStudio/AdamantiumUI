namespace Adamantium.UI.EntityServices;

/// <summary>An overlay stage that records its frame where the tree is laid out, on the loop thread, and only applies it
/// on the thread that draws.</summary>
internal interface IRecordingStage
{
    void Record();
}
