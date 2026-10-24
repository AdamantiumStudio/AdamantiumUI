using System;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Text;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

public class WindowsPlatform : IApplicationPlatform
{
    private Thread uiThread;
    private DispatcherWin32NativeSourceWrapper window;
    private uint dispatchMessage;

    public WindowsPlatform() : this(true)
    {
    }

    /// <summary>A message loop of its own for the splash window when not <paramref name="application"/>, on the thread
    /// that makes it: it claims nothing every window shares - the clipboard, the cursors, the input devices, the file
    /// dialogs, OLE.</summary>
    internal WindowsPlatform(bool application)
    {
        IsApplication = application;
        uiThread = Thread.CurrentThread;
        window = new DispatcherWin32NativeSourceWrapper();
        window.AddHook(WndProc);
        dispatchMessage = Messages.RegisterWindowMessage("DispatcherProcessingMessage");
        if (!application)
        {
            return;
        }

        Clipboard.Current = new WindowsClipboard();   // swap the in-process default for the real OS clipboard
        Cursor.Platform = new WindowsCursors();       // IDC_* shapes for the neutral CursorType catalog

        // Live pointer/key state + the user's double-click speed - one object, three contracts.
        var input = new WindowsInput();
        Mouse.Platform = input;
        Keyboard.Platform = input;
        PlatformSettings.Platform = input;
        Displays.Platform = new WindowsDisplays();
        DesktopWallpaper.Platform = new WindowsDesktopWallpaper();   // what Mica shows: the picture behind the WINDOW
        FileDialog.Platform = new WindowsFileDialog();               // where a file goes is the user's to say
        FileManager.Platform = new ExplorerFileManager();
        WindowsOle.Initialize();   // OLE on THIS (the UI) thread - the precondition for OS drag-drop on every window
    }

    /// <summary>Whether this is the application's loop, not the splash window's own.</summary>
    internal bool IsApplication { get; }

    /// <summary>Runs the message loop until <paramref name="token"/> is cancelled. The application's asks for 1 ms timers
    /// while it runs, so a wait for a frame is not rounded up to the 15.6 ms system tick.</summary>
    public void Run(CancellationToken token)
    {
        if (!IsApplication)
        {
            WaitForMessages(token);
            return;
        }

        Win32Interop.TimeBeginPeriod(1);
        try
        {
            while (!token.IsCancellationRequested)
            {
                while (Messages.PeekMessage(out var msg, IntPtr.Zero, 0, 0, PeekMessageFlag.Remove))
                {
                    Messages.TranslateMessage(ref msg);
                    Messages.DispatchMessage(ref msg);
                }
            }
        }
        finally
        {
            Win32Interop.TimeEndPeriod(1);
        }
    }

    private void WaitForMessages(CancellationToken token)
    {
        using var wake = token.Register(Signal);
        while (!token.IsCancellationRequested && Messages.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Messages.TranslateMessage(ref msg);
            Messages.DispatchMessage(ref msg);
        }
    }

    /// <summary>Destroys the splash window's message window once its loop has ended, on the thread that made it; the
    /// application's lasts as long as the process.</summary>
    internal void Disconnect()
    {
        if (IsApplication)
        {
            throw new InvalidOperationException("The application's message loop is never closed.");
        }

        window.Dispose();
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        WindowMessages message = (WindowMessages) msg;
        if (message == WindowMessages.Destroy)
        {
                
        }
        else if (msg == dispatchMessage)
        {
            Signaled?.Invoke();
        }
            
        return IntPtr.Zero;
    }

    public bool IsOnUIThread => uiThread == Thread.CurrentThread;
    
    public void Signal()
    {
        Messages.PostMessage(window.Handle, dispatchMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public event Action Signaled;

    public IntPtr WindowFromScreenPoint(Adamantium.UI.Core.PixelPoint point) =>
        Win32Interop.WindowFromPoint(new Adamantium.Mathematics.NativePoint((int)point.X, (int)point.Y));

    public static void Initialize(IContainerRegistry resolver)
    {
        resolver.RegisterSingleton<IApplicationPlatform, WindowsPlatform>();
        resolver.RegisterSingleton<IDragGhost, Win32DragGhost>();
        resolver.RegisterSingleton<INativeDragDrop, WindowsDragDrop>();
        resolver.RegisterSingleton<ISyntheticPointerInput, WindowsSyntheticPointerInput>();
        resolver.RegisterSingleton<ISpellChecker, WindowsSpellChecker>();
        resolver.RegisterSingleton<Multiverse.Input.IGamepadBackend, WindowsGamepadBackend>();
    }
}