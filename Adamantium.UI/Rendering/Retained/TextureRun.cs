using Adamantium.Graphics.Core;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>A run of one geometry's textured instances, all sampling one <paramref name="Texture"/> the same way: one is
/// bound per draw, so a run ends where the texture changes.</summary>
internal readonly record struct TextureRun(KeySegment Segment, uint First, uint Count, ITexture Texture,
    SamplerState Sampler);
