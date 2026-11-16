using System;

namespace Adamantium.UI.Rendering;

/// <summary>Makes the GPU resources of a ring - a copy per frame in flight - all of them or none. The GPU's memory can run
/// out halfway, held by another application: what was made is let go of at once - no frame has seen it - and the
/// caller keeps what it had, to try again on a later frame, instead of a ring with holes the next frame trips on.
/// </summary>
internal static class GpuRing
{
    /// <summary><paramref name="count"/> resources from <paramref name="make"/>, given each one's place; throws what
    /// <paramref name="make"/> threw, the ones made before it disposed.</summary>
    public static T[] Make<T>(int count, Func<int, T> make) where T : class, IDisposable
    {
        var ring = new T[count];
        var made = 0;
        try
        {
            for (; made < count; made++)
            {
                ring[made] = make(made);
            }

            return ring;
        }
        catch
        {
            for (var i = 0; i < made; i++)
            {
                ring[i].Dispose();
            }

            throw;
        }
    }
}
