using System.Runtime.InteropServices;

namespace Adamantium.UI.Core.Media.Imaging;

/// <summary>Turns the frames of an animation into one byte per pixel and a shared palette, when all of them together use
/// no more than 256 colors - a quarter of the memory of the full-color frames, with exactly the same pixels.</summary>
internal static class IndexedFrames
{
   public const int PaletteSize = 256;

   /// <summary>Indexes four-byte frames against one palette of at most <see cref="PaletteSize"/> colors. False when the
   /// frames use more colors than that; the caller keeps the full-color frames then.</summary>
   public static bool TryIndex(IReadOnlyList<byte[]> frames, out byte[] palette, out byte[][] indices)
   {
      var paletteIndex = new Dictionary<uint, byte>();
      var colors = new List<uint>(PaletteSize);
      indices = new byte[frames.Count][];
      palette = null;

      for (var layer = 0; layer < frames.Count; layer++)
      {
         var pixels = MemoryMarshal.Cast<byte, uint>(frames[layer]);
         var index = new byte[pixels.Length];
         var lastColor = 0u;
         byte lastIndex = 0;
         var hasLast = false;

         for (var i = 0; i < pixels.Length; i++)
         {
            var color = pixels[i];
            if (!hasLast || color != lastColor)
            {
               if (!paletteIndex.TryGetValue(color, out lastIndex))
               {
                  if (colors.Count == PaletteSize)
                  {
                     indices = null;
                     return false;
                  }

                  lastIndex = (byte)colors.Count;
                  paletteIndex[color] = lastIndex;
                  colors.Add(color);
               }

               lastColor = color;
               hasLast = true;
            }

            index[i] = lastIndex;
         }

         indices[layer] = index;
      }

      palette = new byte[PaletteSize * sizeof(uint)];
      MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(colors)).CopyTo(palette);
      return true;
   }
}
