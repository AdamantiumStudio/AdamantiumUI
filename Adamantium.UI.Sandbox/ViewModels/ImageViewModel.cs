using System.Collections.ObjectModel;
using Adamantium.Core.Collections;
using Adamantium.Imaging;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Sandbox.Content;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Image tab: image formats and animations with one <see cref="Opacity"/> slider, plus a playground for stretch,
/// stretch direction, alignment and box size.</summary>
[ViewModel]
public partial class ImageViewModel : TabPageViewModel
{
    public ImageViewModel() : base("Image") { }

    /// <summary>The animated sources, each with its own playback controls. A collection rather than three hand-wired
    /// blocks: adding one is adding a line here.</summary>
    public ObservableCollection<AnimatedImageViewModel> Animations { get; } =
    [
        new("PNG (APNG animated)", SandboxContentAssets.Textures.APNG_cube, 16),
        new("GIF (infinity)", SandboxContentAssets.Textures.Infinity, 25),
        new("GIF (earth)", SandboxContentAssets.Textures.RotatingEarth2, 25),
        new("PNG (elephant)", SandboxContentAssets.Textures.Elephant, 40)
    ];

    /// <summary>The JPEG 2000 pictures, each shown at the resolution and quality its own sliders choose.</summary>
    public TrackingCollection<Jpeg2000PictureViewModel> Jpeg2000Pictures { get; } =
    [
        new("JPEG 2000: 4 quality layers, 9-7", SandboxContentAssets.Textures.Photo_layers),
        new("JPEG 2000: lossless 5-3, 3 layers", SandboxContentAssets.Textures.Picture_lossless),
    ];

    [Bindable] private double _opacity = 1;

    [Bindable] private Stretch _stretch = Stretch.Uniform;
    [Bindable] private StretchDirection _stretchDirection = StretchDirection.Both;
    [Bindable] private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Stretch;
    [Bindable] private VerticalAlignment _verticalAlignment = VerticalAlignment.Stretch;
    [Bindable] private double _boxWidth = 320;
    [Bindable] private double _boxHeight = 220;

    [Bindable] private double _brightness;
    [Bindable] private double _contrast;
    [Bindable] private double _saturation = 1;

    /// <summary>An 8×8 picture enlarged smoothly - its pixels blend into each other.</summary>
    public ImageSource SmoothPixels { get; } = Pixels(ImageSampling.Smooth);

    /// <summary>The same picture enlarged in squares (<see cref="ImageSampling.Pixelated"/>).</summary>
    public ImageSource PixelatedPixels { get; } = Pixels(ImageSampling.Pixelated);

    // A little heart on a checkered ground, eight pixels a side.
    private static ImageSource Pixels(ImageSampling sampling)
    {
        const string heart = "..XX.XX.,.XXXXXXX,.XXXXXXX,.XXXXXXX,..XXXXX.,...XXX..,....X...,........";
        var rows = heart.Split(',');
        var rgba = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var at = (y * 8 + x) * 4;
                var (r, g, b) = rows[y][x] == 'X' ? (239, 68, 68) : (x + y) % 2 == 0 ? (226, 232, 240) : (148, 163, 184);
                (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = ((byte)r, (byte)g, (byte)b, (byte)255);
            }
        }

        return new BitmapImage(8, 8, 1, 1, SurfaceFormat.R8G8B8A8.UNorm, rgba) { Sampling = sampling };
    }
}
