using System.Collections.ObjectModel;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

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
        new("PNG (APNG animated)", "Textures/APNG-cube.png", 16),
        new("GIF (infinity)", "Textures/infinity.gif", 25),
        new("GIF (earth)", "Textures/RotatingEarth2.gif", 25),
        new("PNG (elephant)", "Textures/elephant.png", 40)
    ];

    [Bindable] private double _opacity = 1;

    [Bindable] private Stretch _stretch = Stretch.Uniform;
    [Bindable] private StretchDirection _stretchDirection = StretchDirection.Both;
    [Bindable] private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Stretch;
    [Bindable] private VerticalAlignment _verticalAlignment = VerticalAlignment.Stretch;
    [Bindable] private double _boxWidth = 320;
    [Bindable] private double _boxHeight = 220;
}
