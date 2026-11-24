namespace Adamantium.UI.Controls;

/// <summary>A listed item a property grid hears, with what its lines read off it. Its own key for what the grid follows:
/// two items equal to each other are still two items to hear.</summary>
internal sealed class FollowedItem
{
    public FollowedItem(HashSet<String> reads)
    {
        Reads = reads;
    }

    public HashSet<String> Reads { get; set; }
}
