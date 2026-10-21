using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>Pictures drawn by the CPU, in memory shared with the compositor.</summary>
internal static unsafe class WaylandShm
{
    /// <summary>An ARGB8888 buffer of premultiplied BGRA pixels; null where none could be made. The memory is never
    /// written again, so the buffer may be destroyed once committed.</summary>
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
