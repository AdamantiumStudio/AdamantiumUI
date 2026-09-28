using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.Models;

/// <summary>A dropped picture as data: its raw bytes plus whether decoding is pending; the view decodes it
/// off-thread.</summary>
[ViewModel]
public partial class DroppedPicture : AdamantiumViewModel
{
    public DroppedPicture(byte[] bytes)
    {
        Bytes = bytes;
    }

    /// <summary>The encoded picture, as it arrived.</summary>
    public byte[] Bytes { get; }

    /// <summary>True until the view has decoded <see cref="Bytes"/> - what the tile's busy indicator follows.</summary>
    [Bindable] private bool _isLoading = true;
}
