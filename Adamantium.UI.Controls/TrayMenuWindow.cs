using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>Where the menu of a <see cref="TrayIcon"/> opens: a menu shows only in a window, so this one, clear and
/// borderless, covers the work area of the monitor the menu was asked on, and the menu opens in it as in any window -
/// placed, flipped at the edges, closed by a press beside it or Escape. It closes as well when another window becomes
/// the active one: a press on the task bar or another application's window never reaches it.</summary>
internal sealed class TrayMenuWindow : Window
{
    private readonly Canvas _room = new() { Background = Brushes.Transparent };
    private ContextMenu _menu;
    private bool _wasActive;

    public TrayMenuWindow()
    {
        ShowWindowBorder = false;
        ResizeMode = WindowResizeMode.NoResize;
        Topmost = true;
        // Kept out of the task bar; activated below, so a press elsewhere takes the activation away.
        ActivateOnShow = false;
        UseTransparentComposition = true;
        Background = Brushes.Transparent;
        Content = _room;
    }

    /// <summary>Opens <paramref name="menu"/> at <paramref name="point"/> of the desktop, in physical pixels.</summary>
    public void Open(ContextMenu menu, PixelPoint point)
    {
        Dismiss();
        var screen = ScreenAt(point);
        if (screen == null)
        {
            return;
        }

        var area = screen.WorkArea;
        var scale = screen.Scale > 0 ? screen.Scale : 1;
        // Sized before it is shown: a window sized after is never laid out.
        Left = area.X;
        Top = area.Y;
        ClientWidth = area.Width / scale;
        ClientHeight = area.Height / scale;
        Show();
        Activate();

        _menu = menu;
        _menu.PropertyChanged += OnMenuPropertyChanged;
        _menu.Open(_room, new Vector2<double>((point.X - area.X) / scale, (point.Y - area.Y) / scale));
    }

    /// <summary>Closes the menu, if open, and the window with it.</summary>
    public void Dismiss()
    {
        if (_menu == null)
        {
            return;
        }

        var menu = _menu;
        _menu = null;
        menu.PropertyChanged -= OnMenuPropertyChanged;
        menu.IsOpen = false;
        _wasActive = false;
        Hide();
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

    private void OnMenuPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ContextMenu.IsOpenProperty && !(bool)e.NewValue)
        {
            Dismiss();
        }
    }

    private static ScreenInfo ScreenAt(PixelPoint point)
    {
        ScreenInfo primary = null;
        foreach (var screen in Displays.Screens)
        {
            var bounds = screen.Bounds;
            if (point.X >= bounds.X && point.X < bounds.X + bounds.Width && point.Y >= bounds.Y
                && point.Y < bounds.Y + bounds.Height)
            {
                return screen;
            }

            if (screen.IsPrimary)
            {
                primary = screen;
            }
        }

        return primary;
    }
}
