using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering.Verification;

internal sealed class RecordSummary
{
    private const int Listed = 50;

    public RecordSummary(long frame, RenderPacket packet)
    {
        Frame = frame;
        Kind = packet.Kind;
        TransformDirty = packet.IsTransformDirty;
        TransformUnknown = packet.TransformUnknown;
        DirtyCount = packet.PartialDirty.Count;
        MovedCount = packet.Moved.Count;
        MotionNodeCount = packet.MovedNodes.Count;
        DrawCount = packet.Draws.Count;
        Dirty = Describe(packet.PartialDirty);
        Moved = Describe(packet.Moved);
        MotionNodes = Describe(packet.MovedNodes);
    }

    public long Frame { get; }

    public RenderBuildKind Kind { get; }

    public bool TransformDirty { get; }

    public bool TransformUnknown { get; }

    public int DirtyCount { get; }

    public int MovedCount { get; }

    public int MotionNodeCount { get; }

    public int DrawCount { get; }

    public IReadOnlyList<string> Dirty { get; }

    public IReadOnlyList<string> Moved { get; }

    public IReadOnlyList<string> MotionNodes { get; }

    private static IReadOnlyList<string> Describe(List<IUIComponent> components)
    {
        var lines = new List<string>(System.Math.Min(components.Count, Listed));
        for (var i = 0; i < components.Count && i < Listed; i++)
        {
            lines.Add(ComponentText.WithWorld(components[i]));
        }

        return lines;
    }
}
