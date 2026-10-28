using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>A pointer at one moment: where it is, in some element's units, and what it reports there.</summary>
public readonly record struct PointerPoint(Pointer Pointer, Vector2<double> Position, PointerPointProperties Properties);
