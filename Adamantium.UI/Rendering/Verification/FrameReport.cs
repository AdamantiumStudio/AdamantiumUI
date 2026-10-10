using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering.Verification;

internal static class FrameReport
{
    private const int Suspects = 40;

    public static void Write(string folder, RecordSummary summary, RenderCache live, RenderCache reference,
        FrameComparison comparison, byte[] livePixels, byte[] walkPixels, byte[] previous, int previousWidth,
        int previousHeight, SurfaceFormat format, bool blueFirst, double scale, MSAALevel msaa, IReadOnlyList<Rect> masks,
        bool images)
    {
        Directory.CreateDirectory(folder);

        var width = comparison.Width;
        var height = comparison.Height;
        if (images)
        {
            var diff = FrameImages.Diff(livePixels, comparison, blueFirst);
            FrameImages.Save(livePixels, width, height, blueFirst, Path.Combine(folder, "live.png"));
            FrameImages.Save(walkPixels, width, height, blueFirst, Path.Combine(folder, "walk.png"));
            FrameImages.Save(diff, width, height, blueFirst, Path.Combine(folder, "diff.png"));

            var zoom = FrameImages.Zoom(livePixels, walkPixels, diff, comparison, out var zoomWidth, out var zoomHeight);
            FrameImages.Save(zoom, zoomWidth, zoomHeight, blueFirst, Path.Combine(folder, "zoom.png"));

            if (previous != null && previousWidth == width && previousHeight == height)
            {
                FrameImages.Save(previous, width, height, blueFirst, Path.Combine(folder, "prev.png"));
            }
        }

        var liveGroups = live.DescribeGroups();
        var referenceGroups = reference.DescribeGroups();
        File.WriteAllText(Path.Combine(folder, "live-groups.txt"), DumpGroups(liveGroups));
        File.WriteAllText(Path.Combine(folder, "walk-groups.txt"), DumpGroups(referenceGroups));

        var box = new Rect(comparison.Left / scale, comparison.Top / scale,
            (comparison.Right - comparison.Left + 1) / scale, (comparison.Bottom - comparison.Top + 1) / scale);

        var text = new StringBuilder();
        text.AppendLine($"frame {summary.Frame} at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        text.AppendLine($"window {width}x{height} px, scale {scale:0.##}, MSAA {msaa}, format {format}");
        text.AppendLine();

        text.AppendLine("HOW THE LIVE FRAME WAS MADE");
        text.AppendLine($"  record: {summary.Kind}, transform dirty {summary.TransformDirty}, transform unknown {summary.TransformUnknown}, draws {summary.DrawCount}");
        text.AppendLine($"  drawn by: {live.LastDrawPath} (replayed {live.LastFrameReplayed}), walk reason {WalkReason(live.LastWalkReason)}");
        text.AppendLine();

        text.AppendLine("WHAT THE FRAME CHANGED");
        List(text, "dirty (re-recorded or repainted)", summary.DirtyCount, summary.Dirty);
        List(text, "moved", summary.MovedCount, summary.Moved);
        List(text, "motion nodes", summary.MotionNodeCount, summary.MotionNodes);
        text.AppendLine();

        text.AppendLine("DIFFERENCE");
        text.AppendLine($"  {comparison.Different} px differ, max channel delta {comparison.MaxDelta}");
        text.AppendLine($"  {comparison.Extra} px only on the live frame (drawn where the walk draws nothing: ghosts, ink past a clip)");
        text.AppendLine($"  {comparison.Missing} px only on the walk (missing or cut on the live frame)");
        text.AppendLine($"  box px [{comparison.Left},{comparison.Top} .. {comparison.Right},{comparison.Bottom}], logical {ComponentText.Box(box)}");
        text.AppendLine($"  masked by popups and adorners: {comparison.Masked} px in {masks.Count} areas");
        foreach (var mask in masks)
        {
            text.AppendLine($"    {ComponentText.Box(mask)}");
        }

        text.AppendLine();

        text.AppendLine("SUSPECTS - groups whose bounds meet the difference");
        var liveHits = liveGroups.Where(g => Meets(g.World, box)).ToList();
        var referenceHits = referenceGroups.Where(g => Meets(g.World, box)).ToList();
        text.AppendLine($"  live ({liveHits.Count}):");
        foreach (var group in liveHits.Take(Suspects))
        {
            text.AppendLine($"    {Describe(group)}");
        }

        text.AppendLine($"  walk ({referenceHits.Count}):");
        foreach (var group in referenceHits.Take(Suspects))
        {
            text.AppendLine($"    {Describe(group)}");
        }

        text.AppendLine();
        text.AppendLine("DIFFERENCES BETWEEN THE TWO CACHES THERE");
        AppendDifferences(text, live, reference, liveHits, referenceHits);

        File.WriteAllText(Path.Combine(folder, "report.txt"), text.ToString());
    }

    private static void AppendDifferences(StringBuilder text, RenderCache liveCache, RenderCache referenceCache,
        List<GroupView> live, List<GroupView> reference)
    {
        var byComponent = reference.GroupBy(g => g.Component).ToDictionary(g => g.Key, g => g.First());
        var found = false;

        foreach (var group in live)
        {
            if (!byComponent.TryGetValue(group.Component, out var twin))
            {
                text.AppendLine($"  only live: {Describe(group)}");
                found = true;
                continue;
            }

            byComponent.Remove(group.Component);
            List<string> notes = [];
            if (!Same(group.World, twin.World))
            {
                notes.Add($"bounds live {ComponentText.Box(group.World)} walk {ComponentText.Box(twin.World)}");
            }

            if (group.Clip.HasValue != twin.Clip.HasValue || group.Clip.HasValue && !Same(group.Clip.Value, twin.Clip.Value))
            {
                notes.Add($"clip live {Clip(group.Clip)} walk {Clip(twin.Clip)}");
            }

            if (group.Units != twin.Units)
            {
                notes.Add($"units live {group.Units} walk {twin.Units}");
            }

            if (Math.Abs(group.Opacity - twin.Opacity) > 1e-3)
            {
                notes.Add($"opacity live {group.Opacity:0.###} walk {twin.Opacity:0.###}");
            }

            if (!group.Current)
            {
                notes.Add("live group STALE (not written by the last walk)");
            }

            if (notes.Count > 0)
            {
                text.AppendLine($"  {ComponentText.Of(group.Component)}: {string.Join("; ", notes)}");
                found = true;
            }

            if (!Same(group.World, twin.World) || group.Clip.HasValue != twin.Clip.HasValue ||
                group.Clip.HasValue && !Same(group.Clip.Value, twin.Clip.Value))
            {
                text.AppendLine("    live chain:" + Chain(liveCache.DescribeChain(group.Component)));
                text.AppendLine("    walk chain:" + Chain(referenceCache.DescribeChain(group.Component)));
            }
        }

        foreach (var twin in byComponent.Values)
        {
            text.AppendLine($"  only walk: {Describe(twin)}");
            found = true;
        }

        if (!found)
        {
            text.AppendLine("  none - the same groups in the same places: the fault is in what the live batches hold, not in the groups");
        }
    }

    private static string DumpGroups(IReadOnlyList<GroupView> groups)
    {
        var text = new StringBuilder();
        foreach (var group in groups)
        {
            text.AppendLine(Describe(group));
        }

        return text.ToString();
    }

    private static string Describe(GroupView group) =>
        $"{ComponentText.Of(group.Component)} {ComponentText.Box(group.World)} clip {Clip(group.Clip)} op {group.Opacity:0.##} " +
        $"units {group.Units} slots {group.Slots}{(group.Current ? string.Empty : " STALE")}";

    private static string Chain(IReadOnlyList<ChainLink> links)
    {
        var text = new StringBuilder();
        foreach (var link in links)
        {
            text.Append(link.Frozen
                ? $"{Environment.NewLine}      {ComponentText.Of(link.Component)} at {link.X:0.#},{link.Y:0.#} " +
                  $"{link.Size.Width:0.#}x{link.Size.Height:0.#}{(link.Clips ? " CLIPS" : string.Empty)}" +
                  $"{(link.ReadLive ? " READ LIVE (no packet carried it)" : string.Empty)}"
                : $"{Environment.NewLine}      {ComponentText.Of(link.Component)} NOT FROZEN - live parent " +
                  ComponentText.Of(link.Component.RenderParent));
        }

        return text.ToString();
    }

    private static string Clip(Rect? clip) => clip.HasValue ? ComponentText.Box(clip.Value) : "none";

    private static void List(StringBuilder text, string title, int count, IReadOnlyList<string> lines)
    {
        text.AppendLine($"  {title}: {count}");
        foreach (var line in lines)
        {
            text.AppendLine($"    {line}");
        }

        if (count > lines.Count)
        {
            text.AppendLine($"    ... and {count - lines.Count} more");
        }
    }

    private static string WalkReason(byte reason) => reason switch
    {
        0 => "none",
        1 => "nothing recorded",
        2 => "stream unusable",
        3 => "transform dirty",
        4 => "layout changed since the record",
        5 => "the splice refused",
        6 => "the slot patch refused",
        _ => reason.ToString()
    };

    private static bool Meets(Rect a, Rect b) =>
        a.X <= b.X + b.Width && a.X + a.Width >= b.X && a.Y <= b.Y + b.Height && a.Y + a.Height >= b.Y;

    private static bool Same(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(a.Width - b.Width) < 0.01 &&
        Math.Abs(a.Height - b.Height) < 0.01;
}
