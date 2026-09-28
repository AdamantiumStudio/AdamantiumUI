using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.Retained;

namespace Adamantium.UI.Rendering;

/// <summary>
/// The baked distance fields halos on arbitrary geometry read, one per distinct SHAPE. Keyed by <see cref="GeometryKey"/>
/// rather than by element: identical meshes already share a key (that is what makes them batch), so a hundred badges of
/// the same outline bake one field between them and the cost is paid once for the life of the renderer.
/// </summary>
internal sealed class HaloFieldCache
{
    private readonly Dictionary<GeometryKey, (ITexture Texture, double Pad)> _fields = new();

    /// <summary>The field for this mesh, baking it on first use. Null only when the mesh has no boundary to measure from.</summary>
    public ITexture GetOrCreate(FrozenMesh mesh, IResourceFactory factory, out double pad)
    {
        pad = 0;
        if (mesh == null || factory == null) return null;

        if (_fields.TryGetValue(mesh.Key, out var cached))
        {
            pad = cached.Pad;
            return cached.Texture;
        }

        if (mesh.Loops is not { Count: > 0 })
        {
            _fields[mesh.Key] = (null, 0);
            return null;
        }

        var pixels = HaloField.Bake(mesh.Loops, mesh.Bounds, out pad);
        // One byte per texel: the field only ever softens a band that is blurry to begin with, so 256 steps across the
        // range is more precision than the eye can use.
        var description = new TextureDescription
        {
            Width = HaloField.Resolution,
            Height = HaloField.Resolution,
            Dimension = TextureDimension.Texture2D,
            Format = SurfaceFormat.R8.UNorm,
            Depth = 1,
            InitialLayout = ImageLayout.Undefined,
            ImageAspect = ImageAspectFlagBits.ColorBit,
            DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            MipLevels = 1,
            ArrayLayers = 1
        };

        var texture = factory.CreateTexture(description, pixels);
        _fields[mesh.Key] = (texture, pad);
        return texture;
    }

    /// <summary>Hands every baked field to <paramref name="device"/>, to be freed once no frame in flight reads it.</summary>
    public void Release(IGraphicsDevice device)
    {
        foreach (var (texture, _) in _fields.Values)
        {
            if (texture is IDisposable disposable)
            {
                device.AddToDeferDisposeQueue(disposable);
            }
        }

        _fields.Clear();
    }
}
