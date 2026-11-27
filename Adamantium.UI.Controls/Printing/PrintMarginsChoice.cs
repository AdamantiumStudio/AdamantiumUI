using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Controls.Printing;

/// <summary>One of the print dialog's margins, as its list offers it: the margins, and the key of its name among the
/// print margins strings - its size in the unit the dialog shows margins in.</summary>
public sealed record PrintMarginsChoice(PrintMargins Margins, string Key)
{
    public override string ToString() => Margins.ToString();
}
