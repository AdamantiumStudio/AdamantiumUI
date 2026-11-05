namespace Adamantium.UI.Core.Media;

/// <summary>What a <see cref="Pen.Thickness"/> is measured in.</summary>
public enum PenThicknessUnit
{
    /// <summary>The units of the space the stroke is drawn in: a zoom makes the line wider.</summary>
    Local,

    /// <summary>Device pixels: the line stays as wide on screen under any scale - a road on a map.</summary>
    DevicePixels
}
