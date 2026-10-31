using Adamantium.UI.Core;

namespace Adamantium.UI.Tabs;

/// <summary>The gesture that tore a tab off, as it stood then: where its pointer was, and whether it was made inside the
/// application - by automation - rather than by a button someone holds.</summary>
internal readonly record struct TornGesture(PixelPoint Pointer, bool IsMade);
