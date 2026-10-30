using Adamantium.Core;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>Pictures drawn by the CPU, in memory shared with the compositor.</summary>
internal static unsafe class WaylandShm
{
    /// <summary>Destroys <paramref name="buffer"/>, committed, once the compositor lets it go (wl_buffer.release). Not at
    /// once: an older compositor takes the picture off its surface the moment its buffer is destroyed - WSLg showed a
    /// cursor for an instant and then none.</summary>
    public static void DestroyWhenReleased(WlBuffer buffer) => buffer.Release += buffer.Destroy;

    /// <summary>An ARGB8888 buffer of premultiplied BGRA pixels; null where none could be made. The memory is never
    /// written again; once committed, the buffer is let go through <see cref="DestroyWhenReleased"/>.</summary>
    public static WlBuffer CreateBuffer(WaylandPlatform platform, byte[] bgra, int width, int height)
    {
        var size = width * height * 4;
        if (platform.Shm == null || bgra == null || width <= 0 || height <= 0 || bgra.Length < size)
        {
            return null;
        }

        var fd = PosixInterop.MemFdCreate("adamantium-shm", PosixInterop.MemFdCloseOnExec);
        if (fd < 0)
        {
            return null;
        }

        try
        {
            fixed (byte* bytes = bgra)
            {
                for (var written = 0; written < size;)
                {
                    var count = (int)PosixInterop.Write(fd, bytes + written, (nuint)(size - written));
                    if (count <= 0)
                    {
                        return null;
                    }

                    written += count;
                }
            }

            var pool = platform.Shm.CreatePool(fd, size);
            var buffer = pool.CreateBuffer(0, width, height, width * 4, WlShm.Format.Argb8888);
            pool.Destroy();
            return buffer;
        }
        finally
        {
            PosixInterop.Close(fd);
        }
    }
}
