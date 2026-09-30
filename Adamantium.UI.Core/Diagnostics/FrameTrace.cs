using System;
using System.Collections.Generic;
using System.Text;

namespace Adamantium.UI.Core.Diagnostics;

/// <summary>TEMPORARY: the draw-pass frames that ran long, kept in MEMORY. The overlay dumps them once a second - the point
/// is that recording costs one list add, so the trace cannot become the thing it measures (a per-frame file append
/// already did that once). Remove with the experiment.</summary>
public static class FrameTrace
{
    public struct Entry
    {
        public double DrawMs;
        public byte Kind;       // RenderBuildKind
        public bool Replayed;
        public int Clones;
        public byte Why;
        public string By;
        public int Cache;       // which RenderCache drew it - several (windows, adorner layers) share this trace
        public double LayoutMs; // the LAYOUT pass and the RECORD pass of the same frame - so a spike can be attributed
        public double BuildMs;
        public int Composited;  // compositor entries applied on this frame (an animation the render thread plays itself)
        public int Ops;         // recorded draw operations replayed
        public int UnitOps;     // ...of which per-unit draws (each its own pipeline + uniforms)
    }

    public static bool Enabled;

    /// <summary>TEMP: the unit type that last cost a frame its patch.</summary>
    public static string Refuser;

    /// <summary>TEMP: why the RECORD fell back to a full walk of the tree. A full walk is the most expensive frame there
    /// is, and "structural" is not a reason - the splice refuses for half a dozen unrelated causes and each has its own
    /// fix. Set at every point that gives up; read on the next frame that is recorded Full.</summary>
    public static string FullWalkReason;

    /// <summary>TEMP: WHAT made the retained stream stale against the layout (why=4). "The layout changed" is not a
    /// cause - a forgiven node move, a resize and a re-parent all land here and none of them is fixed the same way.</summary>
    public static string LayoutChangedBy;

    public static void Add(double drawMs, byte kind, bool replayed, int clones, byte why, int cache, int composited,
        int ops, int unitOps)
    {
        if (!Enabled) return;

        // A ring is the wrong shape for a RARE event: at 450 fps it holds a few seconds, so a hand-made incident (hover,
        // a dropdown) is pushed out while the tester is still typing "done". Frames that ran LONG are kept separately and
        // never evicted. Filtered by COST, not by path: an empty adorner layer walks every single frame at 0.03 ms and
        // filled the whole list with itself in seconds, evicting the room for what was being hunted.
        if (drawMs > 3.0)
        {
            if (_incidents.Count < IncidentLimit)
            {
                _incidents.Add(new Entry
                {
                    DrawMs = drawMs, Kind = kind, Replayed = replayed, Clones = clones, Why = why,
                    By = kind == 3 ? FullWalkReason : why == 4 ? LayoutChangedBy : why is 5 or 6 ? Refuser : null, Cache = cache, Composited = composited, Ops = ops, UnitOps = unitOps,
                    LayoutMs = RuntimeStats.LastLayoutPassMs, BuildMs = RuntimeStats.LastRenderBuildMs
                });
            }
        }
    }

    private const int IncidentLimit = 4096;

    private static readonly System.Collections.Generic.List<Entry> _incidents = new();

    /// <summary>How many long frames have been recorded - a mark a caller can take now and read back from later, so a
    /// report can say "the ones from THIS second" instead of "all of them".</summary>
    public static int IncidentCount => _incidents.Count;

    /// <summary>The long frames recorded since <paramref name="mark"/>.</summary>
    public static string DumpIncidentsSince(int mark)
    {
        var text = new StringBuilder();
        for (var n = Math.Max(0, mark); n < _incidents.Count; n++) Format(text, _incidents[n]);
        return text.ToString();
    }

    /// <summary>Every frame that ran long, oldest-first.</summary>
    public static string DumpIncidents()
    {
        var text = new StringBuilder();
        for (var n = 0; n < _incidents.Count; n++)
        {
            Format(text, _incidents[n]);
        }

        return text.ToString();
    }

    private static void Format(StringBuilder text, Entry e)
    {
        text.Append(e.DrawMs.ToString("F2")).Append(' ')
            .Append(e.Kind).Append(e.Replayed ? " replay" : " walk  ")
            .Append(" clones=").Append(e.Clones).Append(" why=").Append(e.Why)
            .Append(" by=").Append(e.By ?? "-")
            .Append(" cache=").Append(e.Cache).Append(" comp=").Append(e.Composited)
            .Append(" ops=").Append(e.Ops).Append(" unitOps=").Append(e.UnitOps)
            .Append(" layout=").Append(e.LayoutMs.ToString("F2"))
            .Append(" build=").Append(e.BuildMs.ToString("F2")).Append('\n');
    }
}
