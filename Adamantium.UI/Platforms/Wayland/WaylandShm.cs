using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>Pictures the CPU draws - a drag's ghost, its icon - in memory shared with the compositor.</summary>
internal static unsafe class WaylandShm
{
    /// <summary>A buffer holding these pixels; null where the compositor has no wl_shm or the memory could not be
    /// made. ARGB8888 in little-endian memory is the BGRA bytes the framework draws, premultiplied as Wayland takes
    /// them. The memory is never written again, so the buffer may be destroyed as soon as it is committed.</summary>
    public static WlBuffer CreateBuffer(WaylandPlatform platform, byte[] bgra, int width, int height)
    {
        var size = width * height * 4;
        if (platform.Shm == null || bgra == null || width <= 0 || height <= 0 || bgra.Length < size)
        {
            return null;
        }

        var fd = LinuxInterop.memfd_create("adamantium-shm", LinuxInterop.MemFdCloseOnExec);
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
                    var count = (int)LinuxInterop.write(fd, bytes + written, (nuint)(size - written));
                    if (count <= 0)
                    {
                        return null;
                    }

                    written += count;
                }
            }

            // The compositor maps the memory itself; the pool is only how a buffer is cut from it.
            var pool = platform.Shm.CreatePool(fd, size);
            var buffer = pool.CreateBuffer(0, width, height, width * 4, WlShm.Format.Argb8888);
            pool.Destroy();
            return buffer;
        }
        finally
        {
            LinuxInterop.close(fd);
        }
    }
}
