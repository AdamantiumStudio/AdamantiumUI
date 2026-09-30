using System;

namespace Adamantium.UI.Rendering;

// SCRATCH: does the layer key hold? A LAYER is one FlushBatches cycle - the set of draws whose mutual order is
// irrelevant. Counts the layer splits a placement made and the ones it avoided.
public static class LayerProbe
{
    public static long Splits, SplitsAvoided;

    /// <summary>Zero everything - for a test that asks a question about ONE frame it drives itself.</summary>
    public static void Reset()
    {
        Splits = SplitsAvoided = 0;
    }

    /// <summary>ADAM_RENDER_WATCH=&lt;file&gt;: logs a control's draws leaving and re-entering the paint order, to diagnose
    /// controls that vanish while still in the tree. Off by default.</summary>
    public static readonly string WatchPath = Environment.GetEnvironmentVariable("ADAM_RENDER_WATCH");

    private static readonly object WatchLock = new();

    public static void Say(string line)
    {
        if (WatchPath == null) return;

        // The render thread writes these while the UI thread may be writing its own - a probe that loses lines, or
        // throws, is worse than no probe.
        lock (WatchLock) System.IO.File.AppendAllText(WatchPath, line + Environment.NewLine);
    }
}
