using System;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls.Buttons;

/// <summary>A button that drops a list of its items, as a <see cref="DropDown"/> does - but a picked item is an action,
/// not a value it keeps: it runs <see cref="ItemCommand"/> and raises <see cref="ItemClick"/>. Items are
/// <see cref="DropDownButtonItem"/>s, written in place or made for <see cref="ItemsControl.ItemsSource"/>.</summary>
public class DropDownButton : ItemsControl
{
    public static readonly AdamantiumProperty ContentProperty = AdamantiumProperty.Register(nameof(Content),
        typeof(object), typeof(DropDownButton), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ContentTemplateProperty = AdamantiumProperty.Register(nameof(ContentTemplate),
        typeof(DataTemplate), typeof(DropDownButton), new PropertyMetadata(null));

    public static readonly AdamantiumProperty IsDropDownOpenProperty = AdamantiumProperty.Register(nameof(IsDropDownOpen),
        typeof(bool), typeof(DropDownButton),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault, OnIsDropDownOpenChanged));

    public static readonly AdamantiumProperty MaxDropDownHeightProperty = AdamantiumProperty.Register(
        nameof(MaxDropDownHeight), typeof(double), typeof(DropDownButton), new PropertyMetadata(320.0));

    public static readonly AdamantiumProperty ItemCommandProperty = AdamantiumProperty.Register(nameof(ItemCommand),
        typeof(ICommand), typeof(DropDownButton), new PropertyMetadata(null, OnItemCommandChanged));

    public static readonly AdamantiumProperty BackgroundPointerOverProperty = AdamantiumProperty.Register(
        nameof(BackgroundPointerOver), typeof(Brush), typeof(DropDownButton), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty BackgroundPressedProperty = AdamantiumProperty.Register(
        nameof(BackgroundPressed), typeof(Brush), typeof(DropDownButton), new PropertyMetadata(default(Brush)));

    private const InputModifiers Alt = InputModifiers.LeftAlt | InputModifiers.RightAlt;

    private readonly System.Collections.Generic.HashSet<DropDownButtonItem> _styled = new();
    private Popup _popup;
    private DropDownButtonItem _lit;
    private CommandSourceListener<DropDownButton> _itemCommandListener;
    private bool _refusing;

    static DropDownButton()
    {
        FocusableProperty.OverrideMetadata(typeof(DropDownButton), new PropertyMetadata(true));
    }

    /// <summary>What the button shows.</summary>
    public object Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public DataTemplate ContentTemplate
    {
        get => GetValue<DataTemplate>(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>Whether the list is open. A press on the button, Enter, Space, Alt+Down and F4 open it; a pick, Escape and
    /// a press anywhere else close it.</summary>
    public bool IsDropDownOpen
    {
        get => GetValue<bool>(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>The tallest the list grows before it scrolls; 320 by default.</summary>
    public double MaxDropDownHeight
    {
        get => GetValue<double>(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>What a picked item runs, given the item.</summary>
    public ICommand ItemCommand
    {
        get => GetValue<ICommand>(ItemCommandProperty);
        set => SetValue(ItemCommandProperty, value);
    }

    public Brush BackgroundPointerOver
    {
        get => GetValue<Brush>(BackgroundPointerOverProperty);
        set => SetValue(BackgroundPointerOverProperty, value);
    }

    public Brush BackgroundPressed
    {
        get => GetValue<Brush>(BackgroundPressedProperty);
        set => SetValue(BackgroundPressedProperty, value);
    }

    /// <summary>Raised when an item is picked, after its own command and <see cref="ItemCommand"/> ran.</summary>
    public event EventHandler<DropDownButtonItemClickEventArgs> ItemClick;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _popup = GetTemplateChild("PART_Popup") as Popup;
        if (_popup != null)
        {
            _popup.PlacementTarget = this;
            _popup.KeepOpen = false;
            _popup.IgnoreTargetPress = true;
            _popup.DataContext = this;
            _popup.Closed -= OnPopupClosed;
            _popup.Closed += OnPopupClosed;
            _popup.ContentBuilt -= OnPopupContentBuilt;
            _popup.ContentBuilt += OnPopupContentBuilt;
            _popup.IsOpen = IsDropDownOpen;
        }
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.ContentBuilt -= OnPopupContentBuilt;
        }

        _popup = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || e.ChangedButton != MouseButtons.Left || e.Handled)
        {
            return;
        }

        e.Handled = true;
        Focus();
        OnHeaderPressed(e);
    }

    /// <summary>A left press on the button, outside its list: opens or closes the list.</summary>
    protected virtual void OnHeaderPressed(PointerPressedEventArgs e) => Open(!IsDropDownOpen);

    /// <summary>Opens or closes the list as the control's own answer, leaving a binding of <see cref="IsDropDownOpen"/>
    /// in place.</summary>
    protected void Open(bool open) => SetCurrentValue(IsDropDownOpenProperty, open);

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (IsDropDownOpen && !(FocusManager.Focused is DropDownButtonItem row && ReferenceEquals(row.Owner, this)))
        {
            Open(false);
        }
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty && Equals(e.NewValue, false) && IsDropDownOpen)
        {
            Open(false);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEnabled)
        {
            return;
        }

        if (!IsDropDownOpen)
        {
            if (e.Key == Key.F4 || e.Key == Key.DownArrow && (e.Modifiers & Alt) != 0 || OpensOn(e.Key))
            {
                if (!e.IsRepeated) Open(true);
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.DownArrow when (e.Modifiers & Alt) == 0:
            case Key.UpArrow when (e.Modifiers & Alt) == 0:
                Step(e.Key == Key.DownArrow ? 1 : -1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when e.IsRepeated:
                e.Handled = true;
                break;
            case Key.Enter or Key.Space:
                if (_lit is { IsEnabled: true, IsAvailable: true } row)
                {
                    row.PerformClick();
                }
                else
                {
                    Open(false);
                }

                e.Handled = true;
                break;
            case Key.Escape or Key.F4:
            case Key.UpArrow or Key.DownArrow:
                if (!e.IsRepeated) Open(false);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Whether a key on the closed button opens the list: Enter and Space here; a split button runs its body
    /// on them instead.</summary>
    protected virtual bool OpensOn(Key key) => key is Key.Enter or Key.Space;

    protected override AutomationPeer OnCreateAutomationPeer() => new DropDownButtonAutomationPeer(this);

    protected internal override bool IsItemItsOwnContainer(object item) => item is DropDownButtonItem;

    protected internal override IUIComponent GetContainerForItem(object item)
    {
        var container = new DropDownButtonItem { Owner = this, IsMade = true };
        if (ItemContainerStyle != null)
        {
            container.Styles.Add(ItemContainerStyle);
        }

        return container;
    }

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        if (container is not DropDownButtonItem row)
        {
            return;
        }

        row.Owner = this;
        if (!ReferenceEquals(row, item))
        {
            row.DataContext = item;
            row.ContentTemplate = ItemTemplate;
            row.ContentTemplateSelector = ItemTemplateSelector;
            row.Content = item;
        }
        else if (!ReferenceEquals(row.AddedStyle, ItemContainerStyle))
        {
            Unstyle(row);
            if (ItemContainerStyle is { } style && !row.Styles.Contains(style))
            {
                row.Styles.Add(style);
                row.AddedStyle = style;
                _styled.Add(row);
            }
        }

        row.IsAvailable = IsAvailable(row, item);
        row.IsHighlighted = ReferenceEquals(row, _lit);
    }

    protected internal override void ClearContainer(IUIComponent container)
    {
        if (container is DropDownButtonItem row)
        {
            if (ReferenceEquals(row, _lit)) Highlight(null);
            row.IsHighlighted = false;
            if (row.IsMade) row.ClearValue(DataContextProperty);
            Unstyle(row);
        }
    }

    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (_lit != null && ItemContainerGenerator.IndexFromContainer(_lit) < 0) Highlight(null);
        foreach (var gone in e.OldItems ?? Array.Empty<object>())
        {
            if (gone is DropDownButtonItem { Owner: var owner } row && ReferenceEquals(owner, this) && !Items.Contains(row))
            {
                row.Owner = null;
            }
        }
        _styled.RemoveWhere(row =>
        {
            if (Items.Contains(row)) return false;

            row.Styles.Remove(row.AddedStyle);
            row.AddedStyle = null;
            return true;
        });
    }

    private void Unstyle(DropDownButtonItem row)
    {
        if (row.AddedStyle == null) return;

        row.Styles.Remove(row.AddedStyle);
        row.AddedStyle = null;
        _styled.Remove(row);
    }

    internal void HighlightFromContainer(DropDownButtonItem container)
    {
        if (container is { IsEnabled: true, IsAvailable: true }) Highlight(container);
    }

    internal bool Take(DropDownButtonItem container, out object item)
    {
        var index = ItemContainerGenerator.IndexFromContainer(container);
        item = index >= 0 ? Items[index] : container;
        if (index < 0 && container.IsMade) return false;

        Open(false);
        return true;
    }

    internal void Refresh(DropDownButtonItem row)
    {
        var index = ItemContainerGenerator.IndexFromContainer(row);
        if (index >= 0) row.IsAvailable = IsAvailable(row, Items[index]);
        if (ReferenceEquals(row, _lit) && !(row.IsEnabled && row.IsAvailable)) Highlight(null);
    }

    internal void Picked(DropDownButtonItem container, object item)
    {
        if (!IsEnabled) return;

        if (CanRun(item)) CommandManager.ExecuteFrom(ItemCommand, item, this, null);
        ItemClick?.Invoke(this, new DropDownButtonItemClickEventArgs(item));
    }

    private bool CanRun(object item) => ItemCommand is { } command && CommandManager.QueryFrom(command, item, this, null).CanExecute;

    private bool IsAvailable(DropDownButtonItem row, object item) => row.Command != null || ItemCommand == null || CanRun(item);

    private static void OnItemCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var button = (DropDownButton)a;
        button._itemCommandListener ??= new CommandSourceListener<DropDownButton>(button, static b => b.RefreshRows());
        button._itemCommandListener.Listen(e.NewValue as ICommand);
        button.RefreshRows();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _itemCommandListener?.Attached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _itemCommandListener?.Detached();
    }

    private void RefreshRows()
    {
        foreach (var index in ItemContainerGenerator.RealizedIndices)
        {
            if (ItemContainerGenerator.ContainerFromIndex(index) is DropDownButtonItem row)
            {
                row.IsAvailable = IsAvailable(row, Items[index]);
                if (!row.IsAvailable && ReferenceEquals(row, _lit)) Highlight(null);
            }
        }
    }

    private void Step(int delta)
    {
        var count = Items.Count;
        var from = _lit != null ? ItemContainerGenerator.IndexFromContainer(_lit) : -1;
        for (var index = (from < 0 ? (delta > 0 ? -1 : count) : from) + delta; index >= 0 && index < count; index += delta)
        {
            if (ItemContainerGenerator.ContainerFromIndex(index) is DropDownButtonItem { IsEnabled: true, IsAvailable: true } row)
            {
                Highlight(row);
                return;
            }
        }
    }

    private void Highlight(DropDownButtonItem row)
    {
        if (ReferenceEquals(_lit, row)) return;

        if (_lit != null) _lit.IsHighlighted = false;
        _lit = row;
        if (_lit != null) _lit.IsHighlighted = true;
    }

    private void OnPopupContentBuilt(object sender, EventArgs e)
    {
        if (((Popup)sender).FindContentChild("PART_ItemsPresenter") is ItemsPresenter presenter)
        {
            presenter.SetBinding(DataContextProperty, new Core.Data.Binding(nameof(DataContext)) { Source = this });
            ConnectPresenter(presenter);
        }
    }

    private void OnPopupClosed(object sender, EventArgs e) => Open(false);

    private static void OnIsDropDownOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var button = (DropDownButton)a;
        var open = (bool)e.NewValue;
        if (button._refusing) return;

        if (open && !button.IsEnabled)
        {
            button._refusing = true;
            try
            {
                button.Open(false);
            }
            finally
            {
                button._refusing = false;
            }

            LoopSignal.Post(button.TellRefusal);
            return;
        }

        if (button._popup != null) button._popup.IsOpen = open;
        button.Highlight(null);
        button.OnDropDownOpenChanged(open);
        AutomationEvents.RaiseExpandCollapse(button, open);
    }

    private void TellRefusal()
    {
        if (!IsDropDownOpen && Core.Data.BindingEngine.GetBindingExpression(this, IsDropDownOpenProperty) is
                Core.Data.BindingExpression { Mode: Core.Data.BindingMode.TwoWay } binding)
        {
            binding.UpdateSource();
        }
    }

    /// <summary>The list opened or closed.</summary>
    protected virtual void OnDropDownOpenChanged(bool open)
    {
    }
}

/// <summary>The item a <see cref="DropDownButton"/> had picked.</summary>
public class DropDownButtonItemClickEventArgs(object item) : EventArgs
{
    /// <summary>The item as the button's items hold it - the data item, or the row written in place.</summary>
    public object Item { get; } = item;
}
