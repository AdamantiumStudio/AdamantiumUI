using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>Where the menu of a <see cref="TrayIcon"/> opens: a menu shows only in a window, so this one, clear and
/// borderless, is the menu's size and stands at the point it was asked at - turned up or left where the work area ends
/// there. It closes with the menu, picked or put away with Escape, and when another window becomes the active one: a
/// press on the task bar or another application's window never reaches it. One window for one opening, so the
/// application never counts a hidden window.</summary>
public sealed class TrayMenuWindow : Window
{
    private readonly Canvas _room = new() { Background = Brushes.Transparent };
    private ContextMenu _menu;
    private Popup _menuPopup;
    private Size<double> _menuSize;
    private Size<double> _fittedSize;
    private PixelPoint _point;
    private Rect _area;
    private double _scale;
    private bool _wasActive;
    private bool _dismissed;

    internal TrayMenuWindow()
    {
        ShowWindowBorder = false;
        ResizeMode = WindowResizeMode.NoResize;
        // As small as the menu: a window states no least size of its own, and the system gives it one.
        MinWidth = 1;
        MinHeight = 1;
        Topmost = true;
        // Kept out of the task bar; activated below, so a press elsewhere takes the activation away.
        ActivateOnShow = false;
        UseTransparentComposition = true;
        Background = Brushes.Transparent;
        Content = _room;
    }

    /// <summary>Opens <paramref name="menu"/> at <paramref name="point"/> of the desktop, in physical pixels.</summary>
    internal void Open(ContextMenu menu, PixelPoint point)
    {
        var screen = WindowPlacer.ScreenAt(point, Displays.Screens);
        if (screen == null)
        {
            Dismiss();
            return;
        }

        _point = point;
        _area = screen.WorkArea;
        _scale = screen.Scale > 0 ? screen.Scale : 1;
        // Sized before it is shown, a window sized after is never laid out: a point of a window until the menu is
        // measured.
        Left = point.X;
        Top = point.Y;
        ClientWidth = 1;
        ClientHeight = 1;
        Show();
        Activate();

        _menu = menu;
        _menu.PropertyChanged += OnMenuPropertyChanged;
        if (_room.RootVisual != null)
        {
            OpenMenu();
        }
        else
        {
            _room.AttachedToVisualTreeEvent += OnRoomAttached;
        }
    }

    /// <summary>Closes the menu, if open, and the window with it.</summary>
    internal void Dismiss()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        _room.AttachedToVisualTreeEvent -= OnRoomAttached;
        if (_menuPopup != null)
        {
            _menuPopup.LayerPass -= OnMenuLaidOut;
            _menuPopup = null;
        }

        if (_menu is { } menu)
        {
            _menu = null;
            menu.PropertyChanged -= OnMenuPropertyChanged;
            menu.IsOpen = false;
        }

        Close();
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != IsActiveProperty)
        {
            return;
        }

        // Active once it has been: the activation asked for above may come a little after the window shows.
        if (IsActive)
        {
            _wasActive = true;
        }
        else if (_wasActive)
        {
            Dismiss();
        }
    }

    private void OnRoomAttached(object sender, VisualTreeAttachmentEventArgs e)
    {
        _room.AttachedToVisualTreeEvent -= OnRoomAttached;
        OpenMenu();
    }

    private void OpenMenu()
    {
        if (_dismissed || _menu is not { } menu)
        {
            return;
        }

        menu.ApplyCurrentTheme();
        menu.Open(_room, new Vector2<double>(0, 0));
        _menuPopup = menu.Popup;
        if (_menuPopup != null)
        {
            _menuPopup.LayerPass += OnMenuLaidOut;
        }
    }

    // Measured in a window too small for it, the menu takes a scroll bar: its size is followed while it is open, and
    // taken once it holds over two passes.
    private void OnMenuLaidOut(object sender, EventArgs e)
    {
        if (_menuPopup?.ChildValue is not MeasurableUIComponent card)
        {
            return;
        }

        var size = card.DesiredSize;
        var steady = size == _menuSize;
        _menuSize = size;
        if (!steady || !(size.Width > 0) || !(size.Height > 0) || size == _fittedSize)
        {
            return;
        }

        _fittedSize = size;
        if (UIAppContext.Current is { } application)
        {
            application.ExecuteOnUIThreadAsync(() => FitToMenu(size));
        }
        else
        {
            FitToMenu(size);
        }
    }

    private void FitToMenu(Size<double> size)
    {
        if (_dismissed)
        {
            return;
        }

        var width = size.Width * _scale;
        var height = size.Height * _scale;
        var x = _point.X + width <= _area.Right ? _point.X : _point.X - width;
        var y = _point.Y + height <= _area.Bottom ? _point.Y : _point.Y - height;
        Position = new PixelPoint(
            Math.Clamp(x, _area.X, Math.Max(_area.X, _area.Right - width)),
            Math.Clamp(y, _area.Y, Math.Max(_area.Y, _area.Bottom - height)));
        ClientWidth = size.Width;
        ClientHeight = size.Height;
    }

    private void OnMenuPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ContextMenu.IsOpenProperty && !(bool)e.NewValue)
        {
            Dismiss();
        }
    }
}
