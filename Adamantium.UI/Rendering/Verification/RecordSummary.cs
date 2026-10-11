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
        DirtyCount = packet.PartialDirty.Count;
        MovedCount = packet.Moved.Count;
        MotionNodeCount = packet.MovedNodes.Count;
        MotionNodes = Describe(packet.MovedNodes);
    }

    public long Frame { get; }

    public RenderBuildKind Kind { get; }

    public int DirtyCount { get; }

    public int MovedCount { get; }

    public int MotionNodeCount { get; }

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
