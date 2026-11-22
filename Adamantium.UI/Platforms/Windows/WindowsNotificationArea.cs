using System;
using System.Collections.Generic;
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
    private readonly Win32NativeWindowWrapper _window;
    private uint _lastId;

    /// <summary>Made on the pump thread, which the hidden window belongs to.</summary>
    public WindowsNotificationArea()
    {
        _window = new Win32NativeWindowWrapper($"AdamantiumNotificationAreaWindow {Guid.NewGuid()}", 0, 0, 0, 0, 0, 0, 0,
            IntPtr.Zero);
        _window.AddHook(OnMessage);
    }

    public INotificationAreaIcon AddIcon(string toolTip)
    {
        Icon icon;
        lock (_lock)
        {
            icon = new Icon(this, ++_lastId, toolTip);
            _icons.Add(icon.Id, icon);
        }

        icon.Show();
        return icon;
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

        if (happened is NotifyIconEvent.Select or NotifyIconEvent.KeySelect)
        {
            Tell(target.RaiseClicked);
        }
        else if (happened == (uint)WindowMessages.Contextmenu)
        {
            // Where, in the version 4 form: the anchor's screen point in wParam, each half signed.
            var anchor = (long)wParam;
            var point = new PixelPoint((short)(anchor & 0xFFFF), (short)((anchor >> 16) & 0xFFFF));
            Tell(() => target.RaiseMenuAsked(point));
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

    // Raised on the loop thread, where the controls that answer live.
    private static void Tell(Action raise)
    {
        var dispatcher = Threading.Dispatcher.CurrentDispatcher;
        if (dispatcher != null) dispatcher.Post(raise);
        else raise();
    }

    private sealed class Icon(WindowsNotificationArea area, uint id, string toolTip) : INotificationAreaIcon
    {
        private const int TipLength = 127;
        private string _toolTip = toolTip;
        private bool _disposed;

        public uint Id { get; } = id;

        public string ToolTip
        {
            get => _toolTip;
            set
            {
                _toolTip = value;
                if (_disposed)
                {
                    return;
                }

                var data = Data(NotifyIconFlags.Tip | NotifyIconFlags.ShowTip);
                Win32Interop.ShellNotifyIcon(NotifyIconMessage.Modify, ref data);
            }
        }

        public event EventHandler Clicked;

        public event EventHandler<NotificationAreaMenuEventArgs> MenuAsked;

        // Added, or added again after the shell restarted; false while there is no shell to take it - it says when it is
        // back.
        public void Show()
        {
            if (_disposed)
            {
                return;
            }

            var data = Data(NotifyIconFlags.Message | NotifyIconFlags.Icon | NotifyIconFlags.Tip | NotifyIconFlags.ShowTip);
            data.CallbackMessage = IconMessage;
            data.Icon = ApplicationIcon();
            if (!Win32Interop.ShellNotifyIcon(NotifyIconMessage.Add, ref data))
            {
                Log.Logger.Information("The notification area took no icon now; it is put there when the task bar starts");
                return;
            }

            var version = Data(default);
            version.Version = NOTIFYICONDATA.NotifyIconVersion4;
            Win32Interop.ShellNotifyIcon(NotifyIconMessage.SetVersion, ref version);
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            area.Remove(this);
            var data = Data(default);
            Win32Interop.ShellNotifyIcon(NotifyIconMessage.Delete, ref data);
        }

        private NOTIFYICONDATA Data(NotifyIconFlags flags)
        {
            var data = NOTIFYICONDATA.For(area._window.Handle, Id);
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
