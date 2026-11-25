using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>An icon of the application's in the desktop's notification area - the application's own icon, a tool tip, a
/// command run on a click and a menu opened on a right click. Shown while it is one of the application's
/// <c>TrayIcons</c> and <see cref="IsVisible"/>; where the desktop has no such place it shows nowhere.</summary>
public sealed class TrayIcon : FundamentalUIComponent
{
    private readonly ContextMenuRows _menuRows;
    private INotificationAreaIcon _shown;
    private TrayMenuWindow _menuWindow;
    private bool _placed;

    public TrayIcon()
    {
        _menuRows = new ContextMenuRows(this);
    }

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

    /// <summary>Whether the desktop shows the icon now: not while it is not shown, nor on a Linux session whose panel has
    /// not started yet, or that has none.</summary>
    public bool IsDisplayed => _shown?.IsDisplayed == true;

    /// <summary>The icon clicked, or picked from the keyboard.</summary>
    public event EventHandler Clicked;

    /// <summary>Raised on the loop thread when <see cref="IsDisplayed"/> changes - a Linux panel started after the
    /// application, say.</summary>
    public event EventHandler IsDisplayedChanged;

    /// <summary>The window its menu is open in; null while it is closed.</summary>
    internal TrayMenuWindow MenuWindow => _menuWindow;

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
        var displayed = IsDisplayed;
        if (_placed && IsVisible)
        {
            Show();
        }
        else
        {
            Remove();
        }

        TellIfDisplayedChanged(displayed);
    }

    // A menu given where there was none, or taken away: the desktop reads whether an icon has one as it is put there.
    private void ShowAnew()
    {
        var displayed = IsDisplayed;
        Remove();
        Show();
        TellIfDisplayedChanged(displayed);
    }

    private void Show()
    {
        if (_shown != null)
        {
            return;
        }

        // An icon without a menu offers none: a panel shows no empty one on a right click.
        _shown = NotificationArea.AddIcon(ToolTip, ContextMenu != null ? _menuRows : null);
        if (_shown == null)
        {
            return;
        }

        _shown.Clicked += OnClicked;
        _shown.MenuAsked += OnMenuAsked;
        _shown.IsDisplayedChanged += OnIsDisplayedChanged;
    }

    private void Remove()
    {
        if (_shown == null)
        {
            return;
        }

        _shown.Clicked -= OnClicked;
        _shown.MenuAsked -= OnMenuAsked;
        _shown.IsDisplayedChanged -= OnIsDisplayedChanged;
        _shown.Dispose();
        _shown = null;
        CloseMenuWindow();
    }

    private void TellIfDisplayedChanged(bool before)
    {
        if (IsDisplayed != before)
        {
            IsDisplayedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnIsDisplayedChanged(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _shown))
        {
            IsDisplayedChanged?.Invoke(this, EventArgs.Empty);
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

    private static List<NotificationAreaMenuItem> RowsOf(ItemsControl owner)
    {
        List<NotificationAreaMenuItem> rows = [];
        foreach (var item in owner.Items)
        {
            switch (item)
            {
                case Separator or ISeparatorItem { IsSeparator: true }:
                    rows.Add(NotificationAreaMenuItem.Separator);
                    break;
                case MenuItem { Visibility: Visibility.Visible } row:
                    rows.Add(new NotificationAreaMenuItem
                    {
                        Text = row.Header as string ?? row.GetAutomationPeer()?.Name,
                        IsEnabled = row.IsEnabled,
                        IsCheckable = row.IsCheckable,
                        IsChecked = row.IsChecked,
                        Items = row.HasItems ? RowsOf(row) : [],
                        Picked = () => row.Invoke(),
                    });
                    break;
                case MenuItem:
                    break;
                default:
                    // A row of data, which the menu makes a MenuItem of only as it opens: its text, nothing to pick.
                    rows.Add(new NotificationAreaMenuItem { Text = item?.ToString(), IsEnabled = false });
                    break;
            }
        }

        return rows;
    }

    private void OnMenuAsked(object sender, NotificationAreaMenuEventArgs e)
    {
        if (ContextMenu is not { } menu)
        {
            return;
        }

        OnUiThread(() =>
        {
            // A window for each opening: the one before closes with its menu.
            _menuWindow?.Dismiss();
            var window = new TrayMenuWindow();
            window.Closed += OnMenuWindowClosed;
            _menuWindow = window;
            window.Open(menu, e.Point);
        });
    }

    private void OnMenuWindowClosed(object sender, EventArgs e)
    {
        var window = (TrayMenuWindow)sender;
        window.Closed -= OnMenuWindowClosed;
        if (ReferenceEquals(_menuWindow, window))
        {
            _menuWindow = null;
        }
    }

    private void CloseMenuWindow() => OnUiThread(() =>
    {
        _menuWindow?.Dismiss();
        _menuWindow = null;
    });

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

        if (icon._shown != null && (e.OldValue == null) != (e.NewValue == null))
        {
            icon.ShowAnew();
        }
    }

    // For a desktop that shows the menu itself - a Linux panel: the rows of ContextMenu as they stand now, each picked as
    // a click on it would pick it. Read on the loop thread, each time the menu is about to show.
    private sealed class ContextMenuRows(TrayIcon icon) : INotificationAreaMenu
    {
        public IReadOnlyList<NotificationAreaMenuItem> ReadRows() => icon.ContextMenu is { } menu ? RowsOf(menu) : [];
    }
}
