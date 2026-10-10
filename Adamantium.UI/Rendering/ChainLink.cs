using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

/// <summary>One link of the frozen ancestor chain a <see cref="RenderCache"/> composes a component's place from.</summary>
internal readonly record struct ChainLink(IUIComponent Component, bool Frozen, float X, float Y, Size Size, bool Clips,
    bool ReadLive);
