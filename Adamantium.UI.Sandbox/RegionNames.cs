namespace Adamantium.UI.Sandbox;

/// <summary>The app's region names, shared by views (via <c>{x:Static local:RegionNames.BrushStand}</c>) and the
/// view-models navigating into them.</summary>
public static class RegionNames
{
    /// <summary>The brushes tab's stand host: one live stand at a time, navigated in by view key.</summary>
    public const string BrushStand = "BrushStand";

    /// <summary>The text tab's topic host: one topic at a time, navigated in by view key.</summary>
    public const string TextStand = "TextStand";
}
