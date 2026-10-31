using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Core.Media.Imaging;

/// <summary>
/// A picture made of layers of one size, each written on its own, all of them in ONE texture array that starts small and
/// grows as layers are taken - an atlas of map tiles or thumbnails. Shapes filled from its layers with an
/// <see cref="ImageBrush"/> whose source is an <see cref="ImageLayer"/> batch into one draw. Only a rectangle, an ellipse
/// and a regular polygon without a pen draw a layer; other shapes, and the image drawn as a whole, draw nothing.
/// </summary>
public sealed class TextureArrayImage : ImageSource
{
    private const uint FirstCapacity = 16;

    private readonly object _gate = new();
    private readonly List<ImageLayer> _free = [];
    private readonly List<(ImageLayer Layer, byte[] Pixels)> _pending = [];
    private readonly List<TextureRegionUpdate> _regions = [];
    private GrowingTextureArray _layers;
    private uint _taken;
    private uint _maxLayers;
    private int _generation;
    private bool _released;

    /// <summary>Makes an image of up to <paramref name="maxLayers"/> layers of <paramref name="layerWidth"/> ×
    /// <paramref name="layerHeight"/> pixels of <paramref name="format"/> - fewer when the device holds fewer.</summary>
    public TextureArrayImage(uint layerWidth, uint layerHeight, uint maxLayers, SurfaceFormat format)
    {
        if (layerWidth == 0 || layerHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layerWidth), "A layer has pixels.");
        }

        PixelWidth = layerWidth;
        PixelHeight = layerHeight;
        SurfaceLayout = format;
        _maxLayers = Math.Max(2u, maxLayers);
    }

    /// <summary>A layer's width in pixels.</summary>
    public uint PixelWidth { get; }

    /// <summary>A layer's height in pixels.</summary>
    public uint PixelHeight { get; }

    /// <summary>The layers' pixel format.</summary>
    public SurfaceFormat SurfaceLayout { get; }

    public override double Width => PixelWidth;

    public override double Height => PixelHeight;

    /// <summary>The most layers there can be: as asked, or the device's limit once the image has been drawn.</summary>
    public uint MaxLayers
    {
        get
        {
            lock (_gate)
            {
                return _maxLayers;
            }
        }
    }

    /// <summary>Grows by one each time the layers are lost with their texture - the device went - and the image starts
    /// empty again: whoever wrote layers takes and writes them anew.</summary>
    public int Generation => Volatile.Read(ref _generation);

    /// <summary>How many layers are taken now.</summary>
    public int TakenLayers
    {
        get
        {
            lock (_gate)
            {
                return (int)_taken - _free.Count;
            }
        }
    }

    /// <summary>A layer of its own to write and draw; null when <see cref="MaxLayers"/> are taken.</summary>
    public ImageLayer TakeLayer()
    {
        lock (_gate)
        {
            if (_free.Count > 0)
            {
                var reused = _free[^1];
                _free.RemoveAt(_free.Count - 1);
                reused.IsFree = false;
                return reused;
            }

            return _taken < _maxLayers ? new ImageLayer(this, _taken++, _generation) : null;
        }
    }

    /// <summary>Gives <paramref name="layer"/> back to be taken again; what it shows is undefined until it is written.
    /// A layer given back twice, or of another image or generation, is ignored.</summary>
    public void ReleaseLayer(ImageLayer layer)
    {
        lock (_gate)
        {
            if (!Owns(layer) || layer.IsFree)
            {
                return;
            }

            _pending.RemoveAll(write => ReferenceEquals(write.Layer, layer));
            layer.IsFree = true;
            _free.Add(layer);
        }
    }

    /// <summary>Sets the pixels of <paramref name="layer"/>, rows top to bottom in the image's format; they reach the
    /// GPU with the next frame that draws the image. A layer of an earlier generation is ignored.</summary>
    public void Write(ImageLayer layer, byte[] pixels)
    {
        var size = (long)PixelWidth * PixelHeight * SurfaceLayout.SizeInBytes;
        if (pixels == null || pixels.Length != size)
        {
            throw new ArgumentException($"A layer takes {size} bytes.", nameof(pixels));
        }

        lock (_gate)
        {
            if (!Owns(layer) || layer.IsFree)
            {
                return;
            }

            _pending.RemoveAll(write => ReferenceEquals(write.Layer, layer));
            _pending.Add((layer, pixels));
        }
    }

    /// <summary>The array, grown to every layer taken so far, with the layers written since the last frame uploaded;
    /// null once the image is disposed. A texture that died with its device starts the image over (see
    /// <see cref="Generation"/>).</summary>
    public ITexture GetOrCreateTexture(IResourceFactory factory)
    {
        lock (_gate)
        {
            if (_released)
            {
                return null;
            }

            if (_layers?.Texture is { IsDisposed: true })
            {
                StartOver();
            }

            if (_layers == null)
            {
                _layers = factory.CreateGrowingTextureArray(Description(factory), _maxLayers);
                _maxLayers = Math.Min(_maxLayers, _layers.MaxCapacity);
            }

            for (var added = true; added && _layers.Count < _taken;)
            {
                added = _layers.TryAdd(out _);
            }

            _regions.Clear();
            foreach (var (layer, pixels) in _pending)
            {
                if (layer.Index < _layers.Count)
                {
                    _regions.Add(new TextureRegionUpdate(layer.Index, 0, 0, 0, PixelWidth, PixelHeight, pixels));
                }
            }

            _pending.Clear();
            if (_regions.Count > 0)
            {
                factory.UpdateTextureRegions(_layers.Texture, _regions);
            }

            return _layers.Texture;
        }
    }

    protected override void ReleaseUnmanagedResources()
    {
        ITexture texture;
        lock (_gate)
        {
            _released = true;
            texture = _layers?.Texture;
            _layers = null;
            _pending.Clear();
            _free.Clear();
        }

        if (texture?.GraphicsDevice != null && texture is IDisposable deferred)
        {
            texture.GraphicsDevice.AddToDeferDisposeQueue(deferred);
        }
    }

    private bool Owns(ImageLayer layer) => layer != null && ReferenceEquals(layer.Image, this) &&
                                          layer.Generation == _generation;

    private void StartOver()
    {
        _layers = null;
        _taken = 0;
        _free.Clear();
        _pending.Clear();
        Interlocked.Increment(ref _generation);
    }

    private TextureDescription Description(IResourceFactory factory) => new()
    {
        Width = PixelWidth,
        Height = PixelHeight,
        Depth = 1,
        ArrayLayers = Math.Min(FirstCapacity, Math.Min(_maxLayers, Math.Max(2u, factory.MaxTextureArrayLayers))),
        MipLevels = 1,
        Samples = MSAALevel.None,
        Format = SurfaceLayout,
        InitialLayout = ImageLayout.Undefined,
        DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        ImageType = ImageType._2d,
        ImageAspect = ImageAspectFlagBits.ColorBit,
        Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit | ImageUsageFlagBits.TransferSrcBit,
        Dimension = TextureDimension.Texture2D,
    };
}
