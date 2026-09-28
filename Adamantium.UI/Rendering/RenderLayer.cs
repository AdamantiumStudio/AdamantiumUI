namespace Adamantium.UI.Rendering;

// One flush cycle of the recorded frame: draws whose mutual order does not matter, owning a slice of the op stream by
// rank interval. A newcomer in the interval joins it if it overlaps nothing there, else opens a layer right after.
internal sealed class RenderLayer
{
    /// <summary>Paint rank of the first draw in this layer, and of the last. Together they are the layer's PLACE in the
    /// frame - an interval, not a point, because the layer glues everything that fell between two flushes.</summary>
    public long RankFirst = long.MaxValue;

    public long RankLast = long.MinValue;

    /// <summary>Where this layer's ops begin in the recorded stream, and how many there are. Kept as a range rather than
    /// a list of its own so the stream stays one contiguous array to replay - the layer is what the range MEANS.</summary>
    public int OpFirst;

    public int OpCount;

    /// <summary>The batch runs this layer draws: which collector, and which segment of it. One layer holds at most one
    /// run per material (a flush cycle empties each collector once), and their mutual order is the fixed material order,
    /// not the paint ranks - which is precisely why a layer can be treated as one place in the frame.</summary>
    public readonly System.Collections.Generic.List<(byte Batch, int SegId)> Runs = new();

    public void Cover(long rank)
    {
        if (rank < RankFirst) RankFirst = rank;
        if (rank > RankLast) RankLast = rank;
    }

    /// <summary>Whether this rank belongs inside the layer's span. A newcomer here has to be asked the overlap question;
    /// one outside simply goes before or after the whole layer.</summary>
    public bool Covers(long rank) => rank >= RankFirst && rank <= RankLast;

    public override string ToString() => $"layer [{RankFirst}..{RankLast}] ops {OpFirst}+{OpCount}";
}
