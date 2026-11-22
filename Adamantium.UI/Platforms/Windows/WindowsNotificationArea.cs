using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.UI.Core;
using Adamantium.Win32;
using Serilog;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Icons of the task bar's notification area (<c>Shell_NotifyIconW</c>). What is done to them is told to a hidden
/// window of the pump thread - a top-level one, not message-only, as only those hear "TaskbarCreated", the shell saying
/// it started again and the icons have to be put back.</summary>
internal sealed class WindowsNotificationArea : INotificationAreaPlatform
{
    private static readonly uint IconMessage = Messages.RegisterWindowMessage("Adamantium.NotificationAreaIcon");
    private static readonly uint TaskbarCreatedMessage = Messages.RegisterWindowMessage("TaskbarCreated");

    private readonly object _lock = new();
    private readonly Dictionary<uint, Icon> _icons = new();
    private Win32NativeWindowWrapper _window;

    public INotificationAreaIcon AddIcon(string toolTip)
    {
        Icon icon;
        lock (_lock)
        {
            // The lowest free id: the shell tells an icon's id back in 16 bits.
            var id = 1u;
            while (_icons.ContainsKey(id))
            {
                id++;
            }

            icon = new Icon(this, id, toolTip);
            _icons.Add(id, icon);
        }

        // The window is made for the first icon - an application with none pays nothing - on the pump thread, which runs
        // its messages; asked from another thread, the icon is shown there, after the ones asked before it.
        var dispatcher = Threading.Dispatcher.CurrentDispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            MakeWindow();
            icon.Show();
        }
        else
        {
            dispatcher.Invoke(() =>
            {
                MakeWindow();
                icon.Show();
            });
        }

        return icon;
    }

    /// <summary>Lets the shell's "TaskbarCreated" through to <paramref name="window"/>: the shell runs below an elevated
    /// application, and without it the icons would not come back after it restarts.</summary>
    internal static bool HearShellRestarting(IntPtr window)
    {
        var status = CHANGEFILTERSTRUCT.Create();
        return Messages.ChangeWindowMessageFilterEx(window, TaskbarCreatedMessage, MessageFilterAction.Allow, ref status);
    }

    private void MakeWindow()
    {
        if (_window != null)
        {
            return;
        }

        var window = new Win32NativeWindowWrapper($"AdamantiumNotificationAreaWindow {Guid.NewGuid()}", 0, 0, 0, 0, 0, 0,
            0, IntPtr.Zero);
        window.AddHook(OnMessage);
        if (!HearShellRestarting(window.Handle))
        {
            Log.Logger.Warning("The shell restarting will not be heard (error {Error}); icons will not come back after it",
                Marshal.GetLastPInvokeError());
        }

        Volatile.Write(ref _window, window);
    }

    private void Remove(Icon icon)
    {
        lock (_lock)
        {
            _icons.Remove(icon.Id);
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == TaskbarCreatedMessage)
        {
            foreach (var icon in IconsNow())
            {
                icon.Show();
            }

            return IntPtr.Zero;
        }

        if (message != IconMessage)
        {
            return IntPtr.Zero;
        }

        handled = true;
        var told = (long)lParam;
        var happened = (uint)(told & 0xFFFF);
        var id = (uint)((told >> 16) & 0xFFFF);
        Icon target;
        lock (_lock)
        {
            if (!_icons.TryGetValue(id, out target))
            {
                return IntPtr.Zero;
            }
        }

        // Raised on the loop thread, where the controls that answer live.
        if (happened is NotifyIconEvent.Select or NotifyIconEvent.KeySelect)
        {
            Win32WindowWorker.DispatchInput(target.RaiseClicked);
        }
        else if (happened == (uint)WindowMessages.Contextmenu)
        {
            // Where, in the version 4 form: the anchor's screen point in wParam, each half signed.
            var anchor = (long)wParam;
            var point = new PixelPoint((short)(anchor & 0xFFFF), (short)((anchor >> 16) & 0xFFFF));
            Win32WindowWorker.DispatchInput(() => target.RaiseMenuAsked(point));
        }

        return IntPtr.Zero;
    }

    private List<Icon> IconsNow()
    {
        lock (_lock)
        {
            return [.. _icons.Values];
        }
    }

    private sealed class Icon(WindowsNotificationArea area, uint id, string toolTip) : INotificationAreaIcon
    {
        private const int TipLength = 127;

        // Shown, changed and taken away from any thread; the shell is told nothing before the window is made.
        private readonly object _lock = new();
        private string _toolTip = toolTip;
        private bool _disposed;

        public uint Id { get; } = id;

        public string ToolTip
        {
            get => _toolTip;
            set
            {
                lock (_lock)
                {
                    _toolTip = value;
                    if (!HasWindow || _disposed)
                    {
                        return;
                    }

                    var data = Data(NotifyIconFlags.Tip | NotifyIconFlags.ShowTip);
                    Win32Interop.ShellNotifyIcon(NotifyIconMessage.Modify, ref data);
                }
            }
        }

        public event EventHandler Clicked;

        public event EventHandler<NotificationAreaMenuEventArgs> MenuAsked;

        // Added, or added again after the shell restarted; false while there is no shell to take it - it says when it is
        // back.
        public void Show()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                var data = Data(NotifyIconFlags.Message | NotifyIconFlags.Icon | NotifyIconFlags.Tip |
                    NotifyIconFlags.ShowTip);
                data.CallbackMessage = IconMessage;
                data.Icon = ApplicationIcon();
                if (!Win32Interop.ShellNotifyIcon(NotifyIconMessage.Add, ref data))
                {
                    Log.Logger.Information(
                        "The notification area took no icon now; it is put there when the task bar starts");
                    return;
                }

                var version = Data(default);
                version.Version = NOTIFYICONDATA.NotifyIconVersion4;
                Win32Interop.ShellNotifyIcon(NotifyIconMessage.SetVersion, ref version);
            }
        }

        public void RaiseClicked()
        {
            if (!_disposed)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RaiseMenuAsked(PixelPoint point)
        {
            if (!_disposed)
            {
                MenuAsked?.Invoke(this, new NotificationAreaMenuEventArgs(point));
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                area.Remove(this);
                if (!HasWindow)
                {
                    return;
                }

                var data = Data(default);
                Win32Interop.ShellNotifyIcon(NotifyIconMessage.Delete, ref data);
            }
        }

        private bool HasWindow => Volatile.Read(ref area._window) != null;

        private NOTIFYICONDATA Data(NotifyIconFlags flags)
        {
            var data = NOTIFYICONDATA.For(Volatile.Read(ref area._window).Handle, Id);
            data.Flags = flags;
            data.Tip = _toolTip is { Length: > TipLength } ? _toolTip[..TipLength] : _toolTip ?? string.Empty;
            return data;
        }

        // The icon the application's executable carries, at the small size; shared, so never destroyed.
        private static IntPtr ApplicationIcon()
        {
            var size = Win32Interop.GetSystemMetrics(SystemMetrics.Cxsmicon);
            return Win32Interop.LoadImage(Win32Interop.GetModuleHandle(null),
                new IntPtr(Win32NativeWindowWrapper.ApplicationIconId), LoadImageType.Icon, size, size, LoadImageFlags.Shared);
        }
    }
}
