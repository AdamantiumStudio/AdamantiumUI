using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Core.Media.Imaging;

public unsafe class BitmapSource : ImageSource
{
   public BitmapSource()
   {
      DpiXScale = 1;
      DpiYScale = 1;
      Layout = ImageLayout.ShaderReadOnlyOptimal;
   }
   
   public BitmapSource(
      UInt32 width, 
      UInt32 height, 
      double dpiX, 
      double dpiY, 
      SurfaceFormat format,
      byte[] pixels)
   {
      PixelWidth = width;
      PixelHeight = height;
      DpiXScale = dpiX;
      DpiYScale = dpiY;
      SurfaceLayout = format;
      _pixels = pixels;
      Depth = 1;
      Layout = ImageLayout.ShaderReadOnlyOptimal;
   }

   private readonly object _textureLock = new();
   private byte[] _pixels;
   private volatile Fitted _fitted;
   private volatile Refusal _refused;

   /// <summary>The CPU pixel buffer (may be null once uploaded to a GPU texture). For readback consumers - e.g. the
   /// drag ghost, which needs the raw bytes to hand the OS compositor.</summary>
   public byte[] PixelBytes => _pixels;

   public virtual ITexture Texture { get; protected set; }

   public virtual UInt32 PixelWidth { get; protected set; }

   public virtual UInt32 PixelHeight { get; protected set; }

   public virtual SurfaceFormat SurfaceLayout { get; protected set; }
   
   public int Depth { get; set; }

   public override double Width => PixelWidth * DpiXScale;

   public override double Height => PixelHeight * DpiYScale;

   public Double DpiXScale { get; set; }

   public Double DpiYScale { get; set; }
   
   public ImageLayout Layout { get; set; }
   
   public void* NativePointer
   {
      get
      {
         var texture = Texture;
         return texture == null ? null : texture.NativePointer;
      }
   }

   public void Save(Uri filePath, ImageFileType fileType)
   {
      var path = filePath.IsAbsoluteUri ? filePath.AbsolutePath : filePath.LocalPath;
      var pixels = _pixels;
      if (_fitted?.Source == pixels && pixels != null)
      {
         using var image = Adamantium.Imaging.Image.New2D(PixelWidth, PixelHeight, 1, SurfaceLayout);
         Marshal.Copy(pixels, 0, image.DataPointer, pixels.Length);
         image.Save(path, fileType);
         return;
      }

      Texture.Save(path, fileType);
   }
   
   /// <summary>Retires the texture instead of destroying it: frames in flight may still sample it, and freeing it under
   /// them loses the device.</summary>
   protected override void ReleaseUnmanagedResources()
   {
      ITexture texture;
      lock (_textureLock)
      {
         texture = Texture;
         Texture = null;
      }

      Retire(texture);
   }

   /// <summary>Hands <paramref name="texture"/> to its device to free once no frame in flight can sample it - from any
   /// thread, the finalizer's included.</summary>
   protected static void Retire(ITexture texture)
   {
      if (texture == null)
      {
         return;
      }

      if (texture.GraphicsDevice != null && texture is IDisposable deferred)
      {
         texture.GraphicsDevice.AddToDeferDisposeQueue(deferred);
         return;
      }

      texture.Dispose();
   }

   protected void SetPixels(byte[] pixels)
   {
      _pixels = pixels;
   }

   /// <summary>The texture of these pixels on the device <paramref name="factory"/> makes textures for - made when there
   /// is none or it died with its device. Null while the pixels are still coming, once the source is disposed, and
   /// for pixels the device refuses; larger than the device's textures, they go up shrunk to fit, once for every
   /// device of that limit, the picture keeping its own pixels and size.</summary>
   public virtual ITexture GetOrCreateTexture(IResourceFactory factory)
   {
      var current = Texture;
      if (IsDisposed)
      {
         return null;
      }

      if (current is { IsDisposed: false })
      {
         return current;
      }

      var source = _pixels;
      var limit = factory.MaxTextureDimension;
      if (source == null || _refused is { } refused && refused.Source == source && refused.Limit == limit)
      {
         return null;
      }

      var (pixels, width, height) = (source, PixelWidth, PixelHeight);
      if (width > limit || height > limit)
      {
         if (!ImageDownscaler.Supports(SurfaceLayout))
         {
            _refused = new Refusal(source, limit);
            return null;
         }

         var fitted = _fitted;
         if (fitted == null || fitted.Source != source || fitted.Limit != limit)
         {
            var shrunk = ImageDownscaler.FitWithin(source, PixelWidth, PixelHeight, SurfaceLayout, limit, limit,
               out var fittedWidth, out var fittedHeight);
            _fitted = fitted = new Fitted(source, limit, shrunk, fittedWidth, fittedHeight);
         }

         (pixels, width, height) = (fitted.Pixels, fitted.Width, fitted.Height);
      }

      var textureDescription = new TextureDescription
      {
         Width = width,
         Height = height,
         Dimension = TextureDimension.Texture2D,
         Format = SurfaceLayout,
         Depth = 1,
         InitialLayout = ImageLayout.Undefined,
         ImageAspect = ImageAspectFlagBits.ColorBit,
         DesiredImageLayout = Layout,
         MipLevels = 1,
         ArrayLayers = 1
      };

      lock (_textureLock)
      {
         current = Texture;
         if (IsDisposed || current is { IsDisposed: false })
         {
            return IsDisposed ? null : current;
         }

         try
         {
            current = factory.CreateTexture(textureDescription, pixels);
         }
         catch (NotSupportedException)
         {
            _refused = new Refusal(source, limit);
            return null;
         }

         Texture = current;
         return current;
      }
   }

   private sealed record Fitted(byte[] Source, uint Limit, byte[] Pixels, uint Width, uint Height);

   private sealed record Refusal(byte[] Source, uint Limit);
}