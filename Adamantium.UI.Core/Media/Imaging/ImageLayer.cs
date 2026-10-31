namespace Adamantium.UI.Core.Media.Imaging;

/// <summary>One layer of a <see cref="TextureArrayImage"/>: what an <see cref="ImageBrush"/> names to fill a shape with
/// that layer.</summary>
public sealed class ImageLayer : ImageSource
{
    internal ImageLayer(TextureArrayImage image, uint index, int generation)
    {
        Image = image;
        Index = index;
        Generation = generation;
    }

    /// <summary>The image the layer is part of.</summary>
    public TextureArrayImage Image { get; }

    /// <summary>The layer's place in the image's texture array.</summary>
    public uint Index { get; }

    /// <summary>The <see cref="TextureArrayImage.Generation"/> the layer was taken in; a layer of an earlier one is
    /// gone.</summary>
    public int Generation { get; }

    /// <summary>Whether the layer has been given back and waits to be taken again.</summary>
    internal bool IsFree { get; set; }

    public override double Width => Image.Width;

    public override double Height => Image.Height;
}
