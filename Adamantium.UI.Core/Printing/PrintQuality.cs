namespace Adamantium.UI.Core.Printing;

/// <summary>How finely a printer prints: dots to the inch across and down.</summary>
public sealed record PrintQuality(int DotsPerInchX, int DotsPerInchY)
{
    public override string ToString() =>
        DotsPerInchX == DotsPerInchY ? $"{DotsPerInchX} dpi" : $"{DotsPerInchX} × {DotsPerInchY} dpi";
}
