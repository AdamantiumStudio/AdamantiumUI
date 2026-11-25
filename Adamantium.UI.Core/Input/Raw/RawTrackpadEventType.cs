namespace Adamantium.UI.Core.Input.Raw;

/// <summary>Where a gesture on a trackpad is.</summary>
public enum RawTrackpadEventType
{
    Started,
    Delta,
    Completed,

    /// <summary>The fingers are off, and the system goes on with the pan by itself.</summary>
    Inertia
}
