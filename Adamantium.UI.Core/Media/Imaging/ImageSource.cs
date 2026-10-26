using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.TypeParsers;

namespace Adamantium.UI.Core.Media.Imaging;

[TypeParser(typeof(ImageSourceParser))]
[MarkupFile("png", "jpg", "jpeg", "gif", "bmp", "tga", "dds", "ico", "tif", "tiff")]
public abstract class ImageSource : AdamantiumComponent, IDisposable
{
   private int _disposed;

   public abstract double Width { get; }
   public abstract double Height { get; }

   /// <summary>Whether the source is disposed - set before its resources go, so a thread about to make them again sees
   /// it.</summary>
   public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

   protected virtual void ReleaseUnmanagedResources()
   {
   }

   private void Dispose(bool disposing)
   {
      if (Interlocked.Exchange(ref _disposed, 1) == 0)
      {
         ReleaseUnmanagedResources();
      }
   }

   public void Dispose()
   {
      Dispose(true);
      GC.SuppressFinalize(this);
   }

   ~ImageSource()
   {
      Dispose(false);
   }
}
