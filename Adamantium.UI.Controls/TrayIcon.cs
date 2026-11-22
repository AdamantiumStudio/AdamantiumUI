using Adamantium.Core.Commands;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>An icon of the application's in the desktop's notification area - the application's own icon, a tool tip, a
/// command run on a click and a menu opened on a right click. Shown while it is one of the application's
/// <c>TrayIcons</c> and <see cref="IsVisible"/>; where the desktop has no such place it shows nowhere.</summary>
public sealed class TrayIcon : FundamentalUIComponent
{
    private INotificationAreaIcon _shown;
    private TrayMenuWindow _menuWindow;
    private bool _placed;

    public static readonly AdamantiumProperty ToolTipProperty = AdamantiumProperty.Register(nameof(ToolTip),
        typeof(string), typeof(TrayIcon), new PropertyMetadata(null, OnToolTipChanged));

    public static readonly AdamantiumProperty IsVisibleProperty = AdamantiumProperty.Register(nameof(IsVisible),
        typeof(bool), typeof(TrayIcon), new PropertyMetadata(true, OnIsVisibleChanged));

    /// <summary>Opened at the pointer on a right click; a logical child, so it is themed and inherits the DataContext.
    /// </summary>
    public static readonly AdamantiumProperty ContextMenuProperty = AdamantiumProperty.Register(nameof(ContextMenu),
        typeof(ContextMenu), typeof(TrayIcon), new PropertyMetadata(null, OnContextMenuChanged));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(TrayIcon), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CommandParameterProperty = AdamantiumProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(TrayIcon), new PropertyMetadata(null));

    /// <summary>What the icon says when the pointer rests on it.</summary>
    public string ToolTip
    {
        get => GetValue<string>(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    public bool IsVisible
    {
        get => GetValue<bool>(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    public ContextMenu ContextMenu
    {
        get => GetValue<ContextMenu>(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    /// <summary>Run when the icon is clicked or picked from the keyboard.</summary>
    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue<object>(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Whether the icon is in the notification area now.</summary>
    public bool IsShown => _shown != null;

    /// <summary>The icon clicked, or picked from the keyboard.</summary>
    public event EventHandler Clicked;

    /// <summary>One of the application's icons from now on: shown, if visible.</summary>
    internal void Place()
    {
        _placed = true;
        ShowOrTakeAway();
    }

    /// <summary>No longer one of the application's icons: taken away.</summary>
    internal void TakeAway()
    {
        _placed = false;
        ShowOrTakeAway();
    }

    private void ShowOrTakeAway()
    {
        var wanted = _placed && IsVisible;
        if (wanted && _shown == null)
        {
            _shown = NotificationArea.AddIcon(ToolTip);
            if (_shown == null)
            {
                return;
            }

            _shown.Clicked += OnClicked;
            _shown.MenuAsked += OnMenuAsked;
        }
        else if (!wanted && _shown != null)
        {
            _shown.Clicked -= OnClicked;
            _shown.MenuAsked -= OnMenuAsked;
            _shown.Dispose();
            _shown = null;
            CloseMenuWindow();
        }
    }

    private void OnClicked(object sender, EventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
        var parameter = CommandParameter;
        if (Command is { } command && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private void OnMenuAsked(object sender, NotificationAreaMenuEventArgs e)
    {
        if (ContextMenu is not { } menu)
        {
            return;
        }

        OnUiThread(() =>
        {
            _menuWindow ??= new TrayMenuWindow();
            _menuWindow.Open(menu, e.Point);
        });
    }

    private void CloseMenuWindow()
    {
        if (_menuWindow == null)
        {
            return;
        }

        var window = _menuWindow;
        _menuWindow = null;
        OnUiThread(() =>
        {
            window.Dismiss();
            window.Close();
        });
    }

    // Windows are shown, placed and closed on the UI thread.
    private static void OnUiThread(Action action)
    {
        if (UIAppContext.Current is { } application)
        {
            application.ExecuteOnUIThreadAsync(action);
        }
        else
        {
            action();
        }
    }

    private static void OnToolTipChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is TrayIcon { _shown: { } shown })
        {
            shown.ToolTip = (string)e.NewValue;
        }
    }

    private static void OnIsVisibleChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        ((TrayIcon)component).ShowOrTakeAway();

    private static void OnContextMenuChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var icon = (TrayIcon)component;
        if (e.OldValue is ContextMenu old)
        {
            icon.RemoveLogicalChild(old);
        }

        if (e.NewValue is ContextMenu menu)
        {
            icon.AddLogicalChild(menu);
        }
    }
}
