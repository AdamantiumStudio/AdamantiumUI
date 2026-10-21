using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The C library calls the Wayland side needs: the loop's poll over the compositor's socket and the eventfd
/// <see cref="WaylandPlatform.Signal"/> writes to, and the pipes clipboard data travels through.</summary>
internal static unsafe class LinuxInterop
{
    private const string Library = "libc";

    public const short PollIn = 0x001;

    public const int EventFdNonBlock = 0x800;

    public const int EventFdCloseOnExec = 0x80000;

    public const int OpenCloseOnExec = 0x80000;

    public const uint MemFdCloseOnExec = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport(Library, SetLastError = true)]
    public static extern int poll(PollFd* fds, nuint count, int timeout);

    [DllImport(Library, SetLastError = true)]
    public static extern int eventfd(uint initial, int flags);

    [DllImport(Library, SetLastError = true)]
    public static extern nint read(int fd, void* buffer, nuint count);

    [DllImport(Library, SetLastError = true)]
    public static extern nint write(int fd, void* buffer, nuint count);

    [DllImport(Library)]
    public static extern int close(int fd);

    [DllImport(Library, SetLastError = true)]
    public static extern int pipe2(int* fds, int flags);

    [DllImport(Library, SetLastError = true)]
    public static extern int memfd_create(string name, uint flags);
}
