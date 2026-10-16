using System;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Core.Media.Imaging;

/// <summary>
/// An <see cref="ImageSource"/> backed by an externally produced shared surface, imported zero-copy on first render
/// and then sampled like any bitmap.
/// </summary>
public sealed class SharedSurfaceImage : BitmapSource
{
    private readonly SharedSurfaceDescriptor _descriptor;
    private readonly object _importLock = new();
    private int _holders;
    private bool _letGo;
    private SharedSurfaceImage _next;

    public SharedSurfaceImage(SharedSurfaceDescriptor descriptor)
    {
        _descriptor = descriptor;
        PixelWidth = descriptor.Width;
        PixelHeight = descriptor.Height;
        SurfaceLayout = descriptor.Format;
    }

    public SharedSurfaceDescriptor Descriptor => _descriptor;

    public override ITexture GetOrCreateTexture(IResourceFactory factory)
    {
        lock (_importLock)
        {
            return _letGo ? null : Import(factory);
        }
    }

    /// <summary>Lets go of this image in favor of <paramref name="next"/> (or of nothing): a frame recorded with this
    /// image draws <paramref name="next"/> instead, since the producer replaces surfaces faster than frames recorded
    /// with them are drawn.</summary>
    public void ReplaceWith(SharedSurfaceImage next)
    {
        lock (_importLock)
        {
            _next = next;
        }
        Dispose();
    }

    /// <summary>
    /// Hands the import of the newest image this one was replaced by to a component that samples it, importing on
    /// first use; <c>null</c> when there is nothing to sample. <paramref name="holder"/> is the image held, and each
    /// hold is ended by its <see cref="ReleaseImport"/>.
    /// </summary>
    public ITexture HoldImport(IResourceFactory factory, out SharedSurfaceImage holder)
    {
        var image = this;
        while (true)
        {
            lock (image._importLock)
            {
                if (image._next == null)
                {
                    var texture = image._letGo ? image.Texture : image.Import(factory);
                    if (texture is { IsDisposed: false })
                    {
                        image._holders++;
                        holder = image;
                        return texture;
                    }

                    holder = null;
                    return null;
                }

                image = image._next;
            }
        }
    }

    /// <summary>Ends a <see cref="HoldImport"/> once no frame of the holder can sample the import any more. The last
    /// hold of an image already let go of frees the import.</summary>
    public void ReleaseImport()
    {
        lock (_importLock)
        {
            _holders--;
            if (_holders == 0 && _letGo) FreeImport();
        }
    }

    private ITexture Import(IResourceFactory factory)
    {
        // The import died with its device, and so did the producer's surface: nothing to import again until the
        // producer hands over a surface made on the new device.
        if (Texture is { IsDisposed: true })
        {
            return null;
        }

        return Texture ??= factory.ImportSharedSurface(_descriptor);
    }

    // Held imports are freed by their last holder, which releases only once its frames are done.
    protected override void ReleaseUnmanagedResources()
    {
        lock (_importLock)
        {
            _letGo = true;
            if (_holders == 0) FreeImport();
        }
    }

    private void FreeImport()
    {
        var texture = Texture;
        Texture = null;
        if (texture is { IsDisposed: false }) Retire(texture);
    }
}
