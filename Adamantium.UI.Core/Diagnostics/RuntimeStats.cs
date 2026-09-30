namespace Adamantium.UI.Core.Diagnostics;

/// <summary>Live counters for the diagnostics overlay, sampled once per frame. Last-pass fields snapshot the latest pass;
/// cumulative counters are read as per-frame deltas.</summary>
public static class RuntimeStats
{
    /// <summary>Wall-clock duration of the most recent layout pass, in milliseconds (~0 on an idle frame, which is the
    /// whole point of the dirty-queue model: no per-frame tree walk).</summary>
    public static double LastLayoutPassMs;

    /// <summary>True if the most recent layout pass hit the frame budget and deferred work to a later frame.</summary>
    public static bool LastPassBudgetDeferred;

    // Per-frame render-pipeline phase timings (ms), snapshots of the most recent frame. They make the
    // otherwise-invisible per-frame render cost measurable, so the
    // retained rewrite can be aimed at the phase that actually dominates (today the per-frame cache REBUILD, not layout).
    /// <summary>RenderCache.BuildFromVisualTree - the per-frame walk that re-records every component's draw commands.</summary>
    public static double LastRenderBuildMs;
    /// <summary>RenderCache.ProcessCommands - re-bake every cached unit's world transform.</summary>
    /// <summary>The two halves of the render BUILD, so a spike says WHICH: the device-free record (walk + component.Render
    /// into the packet) or the apply that realizes it into units.</summary>
    public static double LastRecordMs;
    public static double LastApplyMs;

    /// <summary>The RECORD half, split the same way: how long the components' own Render calls took and how many draws came
    /// out empty.</summary>
    public static double LastRecordRenderMs;
    public static int LastRecordEmptyDraws;

    /// <summary>The rest of the record, so the four parts can be held against LastRecordMs. Timing only the structural
    /// placement path is how a 432ms record came to report zero of everything: a resize re-records through the
    /// geometry-dirty path, which places nothing.</summary>
    public static double LastRecordPlanMs;
    public static double LastRecordCopyMs;
    public static double LastRecordSnapMs;
    public static int LastRecordDirty;
    public static int LastRecordClassifySkips;

    /// <summary>Guards the histograms, which are written on render-side threads and read by the probe; a torn Dictionary
    /// crashes its reader.</summary>
    public static readonly object HistogramLock = new();

    /// <summary>...and which types the record actually SPENDS its time in, in ms. Skipping the components that draw
    /// nothing left the record at 188ms over ~5300 real records - 35us each - and no count says which of them that is.</summary>
    public static readonly System.Collections.Generic.Dictionary<System.Type, double> RecordMsByType = new();

    public static void NoteRecordMs(System.Type type, double ms)
    {
        lock (HistogramLock)
        {
            RecordMsByType.TryGetValue(type, out var t);
            RecordMsByType[type] = t + ms;
        }
    }

    /// <summary>The snapshot freeze, split by WHICH loop: the packet's re-records, or the whole geometry-dirty set (which
    /// the skip deliberately left intact - a container that clips must clip at its new size). Predicting that the second
    /// would fall with the first was wrong; the two are separately measured now.</summary>
    public static double LastSnapDrawsMs;
    public static double LastSnapDirtyMs;
    public static double LastSnapOpacityMs;
    public static double LastSnapTailMs;
    public static int LastSnapPublished;

    /// <summary>...and how much each half ALLOCATES. A record that allocates 1.7MB a frame pays for it later as a GC
    /// pause, which no stage timer inside the frame can see - the whole-loop lesson again.</summary>
    public static long LastRecordRenderBytes;
    public static long LastRecordCopyBytes;
    public static long LastSnapBytes;

    /// <summary>The structural PLAN on its own, apart from the dirty-set pre-validation the same timer used to cover, and
    /// how many structural marks it had to place. A tile drag churns containers (park/unpark/rebind), so this is the
    /// cost of deciding WHERE things go rather than of drawing them.</summary>
    public static double LastRecordPlanOnlyMs;
    public static int LastRecordStructuralMarks;

    /// <summary>What the placement actually TOUCHED. A frame that placed TWO structural marks and spent 77ms doing it is
    /// not paying per mark, so the question is how many sibling lists it walked - the same "runs x children" shape a
    /// neighbor-map rewrite already fixed once in this file. Scans is the sum of every child list re-read.</summary>
    public static long LastRecordPlanScans;

    /// <summary>...and WHICH of the three sibling-list readers did it. One counter said a million children were re-read
    /// to place 678; three say which helper to fix.</summary>
    public static long ScansSuccessor;    // SuccessorRank - walks UP, re-reading every ancestor level's children
    public static long ScansLastRank;     // TryLastRankOfSubtree - walks DOWN the previous sibling's subtree
    public static long ScansCollect;      // CollectSubtreeInPaintOrder - the placed subtree itself
    public static long ScansParent;       // the one list PlanNewChildren legitimately needs

    public static int LastRecordPlanRuns;
    public static int LastRecordPlanParents;

    /// <summary>What the apply did this frame: units built, units updated in place, and commands consumed.</summary>
    public static long UnitsCreated;
    public static long UnitsUpdated;
    public static long CommandsApplied;

    public static double LastRenderProcMs;
    /// <summary>RenderCache.Render - the content draw pass (batch + command recording).</summary>
    public static double LastRenderDrawMs;

    /// <summary>TEMP: the out-of-pass PreRender sweep - it visits every unit of every group on EVERY frame, so whether
    /// that matters is a question a number answers, not a guess.</summary>
    public static double LastPreRenderMs;

    /// <summary>The draw split in RenderCore order: setup (transform table, clip slots), paint (animations, repaints),
    /// moved (motion-node matrices) and ops (replaying the stream).</summary>
    public static double LastDrawSetupMs;
    public static double LastDrawPaintMs;
    public static double LastDrawMovedMs;
    public static double LastDrawOpsMs;

    /// <summary>...and the fifth: the WALK, taken when none of the replay/patch paths qualified. O(scene) where they are
    /// O(dirty). Its own number because the four above keep their previous frame's values on a walking frame, and
    /// reading them then reports an expensive walk as a cheap replay.</summary>
    public static double LastDrawWalkMs;

    /// <summary>Whether the last draw replayed the recorded stream instead of walking - the one bit that says which of
    /// the numbers above describe it.</summary>
    public static bool LastDrawReplayed;

    /// <summary>Paint, split into the three calls it is made of - see RenderCache's copies.</summary>
    public static double LastDrawAnimMs;
    public static double LastDrawArenaPaintMs;
    public static double LastDrawBrushRepaintMs;
    /// <summary>Overlay stages (adorner + popup) draw.</summary>
    public static double LastProcessorsMs;

    /// <summary>The four steps of a frame that are NOT recording or drawing content, and which together were most of a
    /// frame while nobody was looking: BeginDraw (its fence wait is where a frame waits for the GPU to be done with the
    /// slot, plus the acquire), EndDraw (finalize + blit to the swapchain image), Submit (hand the queue the work), and
    /// Present. Named separately because they have four different fixes and only one of them is "the GPU is busy".</summary>
    public static double LastBeginDrawMs;
    public static double LastEndDrawMs;
    public static double LastSubmitMs;
    public static double LastPresentMs;

    /// <summary>Cumulative count of frames actually PRESENTED. With a dedicated render thread this is the only honest frame
    /// rate: the loop's own rate measures Update + record, and the two are deliberately decoupled - a heavy Update must not
    /// drag the presented frame rate down with it (that is the entire point of the split). Sample by delta.</summary>
    public static long PresentedFrames;

    /// <summary>Cumulative count of binding target writes - every time a <c>{Binding}</c> pushes a value to its target:
    /// the initial connect, a DataContext re-resolve (e.g. a recycled list container rebinding on scroll), AND a batched
    /// source-property change. Sample by delta to see how many landed this frame (idle ~0; spikes on scroll rebinds and
    /// on a binding storm, where the per-flush cap bounds it).</summary>
    public static long BindingUpdatesApplied;
}
