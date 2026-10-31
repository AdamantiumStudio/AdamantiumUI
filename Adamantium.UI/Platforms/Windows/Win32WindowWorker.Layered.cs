using System;
using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

internal partial class Win32WindowWorker
{
    private readonly object _frameLock = new();
    private volatile byte _frameOpacity = 255;
    private bool _frameReleased;
    private volatile bool _framesComposed;
    private IntPtr _frameDc;
    private IntPtr _frameBitmap;
    private IntPtr _frameBits;
    private IntPtr _frameOldBitmap;
    private uint _frameWidth;
    private uint _frameHeight;

    public bool ComposesFromPixels => true;

    /// <summary>Puts the frame onto the window, which becomes a layered one with its first frame, with the alpha of each
    /// pixel and the window's opacity; called on the render thread.</summary>
    public unsafe void Compose(Image frame)
    {
        var buffer = frame.PixelBuffer[0];
        var pixels = buffer.DataPointer;
        var width = buffer.Width;
        var height = buffer.Height;
        var stride = (uint)buffer.RowStride;
        if (!_framesComposed)
        {
            UIContext.Dispatcher.Invoke(BeginComposing);
        }

        lock (_frameLock)
        {
            var handle = source?.Handle ?? IntPtr.Zero;
            if (!_framesComposed || _frameReleased || handle == IntPtr.Zero || width == 0 || height == 0)
            {
                return;
            }

            EnsureFrame(width, height);
            var row = width * 4;
            for (var y = 0; y < height; y++)
            {
                Buffer.MemoryCopy((byte*)pixels + y * stride, (byte*)_frameBits + y * row, row, row);
            }

            var size = new NativeSize((int)width, (int)height);
            var origin = new NativePoint { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = Win32Interop.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = _frameOpacity,
                AlphaFormat = Win32Interop.AC_SRC_ALPHA
            };

            Win32Interop.UpdateLayeredWindow(handle, IntPtr.Zero, IntPtr.Zero, ref size, _frameDc, ref origin, 0,
                ref blend, Win32Interop.ULW_ALPHA);
        }
    }

    private void BeginComposing()
    {
        var handle = source?.Handle ?? IntPtr.Zero;
        if (_framesComposed || handle == IntPtr.Zero || !window.UseTransparentComposition)
        {
            return;
        }

        var style = (WindowStyleEx)Win32Interop.GetWindowLong(handle, WindowLongType.Exstyle).ToInt64();
        if (style.HasFlag(WindowStyleEx.Layered))
        {
            Win32Interop.SetWindowLong(handle, WindowLongType.Exstyle, new IntPtr((long)(style & ~WindowStyleEx.Layered)));
        }

        Win32Interop.SetWindowLong(handle, WindowLongType.Exstyle, new IntPtr((long)(style | WindowStyleEx.Layered)));
        _framesComposed = true;
    }

    private void EnsureFrame(uint width, uint height)
    {
        if (_frameBitmap != IntPtr.Zero && _frameWidth == width && _frameHeight == height)
        {
            return;
        }

        ReleaseFrameLocked();
        var screenDc = Win32Interop.GetDC(IntPtr.Zero);
        _frameDc = Win32Interop.CreateCompatibleDC(screenDc);
        Win32Interop.ReleaseDC(IntPtr.Zero, screenDc);

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = (int)width,
            biHeight = -(int)height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Win32Interop.BI_RGB
        };

        _frameBitmap = Win32Interop.CreateDIBSection(_frameDc, ref header, Win32Interop.DIB_RGB_COLORS, out _frameBits,
            IntPtr.Zero, 0);
        _frameOldBitmap = Win32Interop.SelectObject(_frameDc, _frameBitmap);
        _frameWidth = width;
        _frameHeight = height;
    }

    private void ReleaseFrame()
    {
        lock (_frameLock)
        {
            _frameReleased = true;
            ReleaseFrameLocked();
        }
    }

    private void ReleaseFrameLocked()
    {
        if (_frameDc != IntPtr.Zero)
        {
            Win32Interop.SelectObject(_frameDc, _frameOldBitmap);
            Win32Interop.DeleteDC(_frameDc);
        }

        if (_frameBitmap != IntPtr.Zero)
        {
            Win32Interop.DeleteObject(_frameBitmap);
        }

        _frameDc = IntPtr.Zero;
        _frameBitmap = IntPtr.Zero;
        _frameBits = IntPtr.Zero;
        _frameOldBitmap = IntPtr.Zero;
        _frameWidth = 0;
        _frameHeight = 0;
    }
}
