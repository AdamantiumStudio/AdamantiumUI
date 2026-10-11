using System.Runtime.InteropServices;

namespace Adamantium.UI.Core.Media.Imaging;

/// <summary>Turns the frames of an animation into one byte per pixel and a shared palette, when all of them together use
/// no more than 256 colors - a quarter of the memory of the full-color frames, with exactly the same pixels. Frames are
/// indexed one at a time, so the full-color ones never have to be held together.</summary>
internal sealed class IndexedFrames
{
   public const int PaletteSize = 256;

   private readonly Dictionary<uint, byte> _paletteIndex = new();
   private readonly List<uint> _colors = new(PaletteSize);

   public byte[] TryIndex(ReadOnlySpan<byte> frame)
   {
      var pixels = MemoryMarshal.Cast<byte, uint>(frame);
      var index = new byte[pixels.Length];
      var lastColor = 0u;
      byte lastIndex = 0;
      var hasLast = false;

      for (var i = 0; i < pixels.Length; i++)
      {
         var color = pixels[i];
         if (!hasLast || color != lastColor)
         {
            if (!_paletteIndex.TryGetValue(color, out lastIndex))
            {
               if (_colors.Count == PaletteSize)
               {
                  return null;
               }

               lastIndex = (byte)_colors.Count;
               _paletteIndex[color] = lastIndex;
               _colors.Add(color);
            }

            lastColor = color;
            hasLast = true;
         }

         index[i] = lastIndex;
      }

      return index;
   }

   public byte[] Palette()
   {
      var palette = new byte[PaletteSize * sizeof(uint)];
      MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_colors)).CopyTo(palette);
      return palette;
   }
}
