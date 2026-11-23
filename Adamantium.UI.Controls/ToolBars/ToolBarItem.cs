using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>
/// The container of one command of a <see cref="ToolBar"/> or a <see cref="ToolBarGroup"/>, as a list's item is: it shows
/// the command in the <see cref="Form"/> its bar decides. Along a bar, and in its overflow menu, that is the command itself.
/// On the column of a bar down the side, each command stands as its <see cref="ToolBar.VerticalFormProperty"/> says: in its
/// own narrow form while it fits the column (<see cref="ToolBar.CommandOrientationProperty"/> tells its theme), folded
/// into a <see cref="ToolBarFoldButton"/> when it does not, and a label tied to a command by <c>LabeledBy</c> leaves the
/// column for the command's tooltip and the header of its card.
/// </summary>
public class ToolBarItem : ContentControl
{
    private static readonly AdamantiumPropertyKey FormPropertyKey = AdamantiumProperty.RegisterReadOnly(nameof(Form),
        typeof(ToolBarItemForm), typeof(ToolBarItem),
        new PropertyMetadata(ToolBarItemForm.Shown, PropertyMetadataOptions.AffectsMeasure,
            static (a, _) => ((ToolBarItem)a).OnFormChanged()));

    public static readonly AdamantiumProperty FormProperty = FormPropertyKey.Property;

    private static readonly AdamantiumPropertyKey LabelPropertyKey = AdamantiumProperty.RegisterReadOnly(nameof(Label),
        typeof(object), typeof(ToolBarItem),
        new PropertyMetadata(null, static (a, _) => ((ToolBarItem)a).OnLabelChanged()));

    public static readonly AdamantiumProperty LabelProperty = LabelPropertyKey.Property;

    private ToolBarFoldButton _foldButton;
    private IUIComponent _labelSource;
    private bool _isLabel;
    private bool _tipFromLabel;

    /// <summary>How the command is shown now: decided by its bar, never by an author.</summary>
    public ToolBarItemForm Form
    {
        get => GetValue<ToolBarItemForm>(FormProperty);
        private set => SetValue(FormPropertyKey, value);
    }

    /// <summary>The text of the label tied to the command by <c>LabeledBy</c>, while the command stands on a column: the
    /// label has left the column for it. Null elsewhere.</summary>
    public object Label
    {
        get => GetValue(LabelProperty);
        private set => SetValue(LabelPropertyKey, value);
    }

    /// <summary>The button the command is folded into, once it has been.</summary>
    internal ToolBarFoldButton FoldButton => _foldButton;

    /// <summary>Whether the folded command's card is open beside the column.</summary>
    public bool IsCardOpen
    {
        get => GetValue<bool>(IsCardOpenProperty);
        set => SetValue(IsCardOpenProperty, value);
    }

    public static readonly AdamantiumProperty IsCardOpenProperty = AdamantiumProperty.Register(nameof(IsCardOpen),
        typeof(bool), typeof(ToolBarItem),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault,
            static (a, _) => ((ToolBarItem)a).OnIsCardOpenChanged()));

    private Popup _card;
    private ContentPresenter _cardPresenter;
    private ContentPresenter _cardHeader;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        HookCard(GetTemplateChild("PART_Card") as Popup);
        Host();
        // A new template's card, closed, is told what the item is in.
        OnIsCardOpenChanged();
    }

    public override void OnRemoveTemplate()
    {
        HookCard(null);
        base.OnRemoveTemplate();
    }

    // The card is built on its first opening, so its parts arrive with its content - see RibbonGroup's flyout.
    private void HookCard(Popup card)
    {
        if (_card != null)
        {
            _card.ContentBuilt -= OnCardBuilt;
            _card.Closed -= OnCardClosed;
            if (_cardPresenter != null)
            {
                _cardPresenter.Content = null;
            }
        }

        _card = card;
        _cardPresenter = null;
        _cardHeader = null;
        if (_card != null)
        {
            _card.ContentBuilt += OnCardBuilt;
            _card.Closed += OnCardClosed;
        }
    }

    private void OnCardBuilt(object sender, System.EventArgs e)
    {
        _cardPresenter = _card?.FindContentChild("PART_CardPresenter") as ContentPresenter;
        _cardHeader = _card?.FindContentChild("PART_CardHeader") as ContentPresenter;
        Host();
    }

    private void OnCardClosed(object sender, System.EventArgs e) => SetCurrentValue(IsCardOpenProperty, false);

    private void OnIsCardOpenChanged()
    {
        var open = IsCardOpen && Form == ToolBarItemForm.Folded;
        if (_card != null)
        {
            _card.IsOpen = open;
        }

        _foldButton?.SetCurrentValue(Primitives.ToggleButton.IsCheckedProperty, open);
    }

    private void OnFoldButtonClick(object sender, RoutedEventArgs e) =>
        SetCurrentValue(IsCardOpenProperty, _foldButton?.IsChecked == true);

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Host();
    }

    protected override Core.Automation.AutomationPeer OnCreateAutomationPeer() => null;

    /// <summary>A new item for a bar's or a section's command, styled by their <paramref name="itemContainerStyle"/>.</summary>
    internal static ToolBarItem Create(Style itemContainerStyle)
    {
        var item = new ToolBarItem();
        // Into Styles, applied after the theme - see ItemsControl.GetContainerForItem.
        if (itemContainerStyle != null)
        {
            item.Styles.Add(itemContainerStyle);
        }

        return item;
    }

    /// <summary>Whether <paramref name="item"/> of a bar or a section needs no item to hold it.</summary>
    internal static bool IsOwnContainer(object item) => item is ToolBarItem or ToolBarGroup;

    /// <summary>Puts <paramref name="item"/> of a bar or a section into its <paramref name="container"/>: a control as it
    /// is, under the bar's data; data as <paramref name="template"/> draws it.</summary>
    internal static void Prepare(IUIComponent container, object item, DataTemplate template)
    {
        if (ReferenceEquals(container, item) || container is not ToolBarItem holder)
        {
            return;
        }

        if (item is IUIComponent command)
        {
            // Shown or hidden as its command is, so the bar's spacing and rules skip a hidden one as before.
            holder.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = command });
        }
        else
        {
            holder.DataContext = item;
            holder.ContentTemplate = template;
        }

        holder.Content = item;
    }

    /// <summary>Lets go of what <paramref name="container"/> held, as its bar or section lets it go: the command, free for
    /// its next item, and the binding to it, so the command - which outlives the item - does not hold it.</summary>
    internal static void Release(IUIComponent container)
    {
        if (container is not ToolBarItem holder)
        {
            return;
        }

        holder.RemoveBinding(VisibilityProperty);
        holder.ClearValue(VisibilityProperty);
        holder.SetCurrentValue(IsCardOpenProperty, false);
        if (holder.Content is IUIComponent)
        {
            holder.Content = null;
        }
        else
        {
            holder.DataContext = null;
        }
    }

    /// <summary>The command a bar's or a section's <paramref name="command"/> stands for: what an item holds, else itself.
    /// Its settings - where it overflows to, how it stands down the side - are written on it, not on its item.</summary>
    internal static IMeasurableComponent CommandOf(IMeasurableComponent command) =>
        command is ToolBarItem { Content: IMeasurableComponent held } ? held : command;

    static ToolBarItem()
    {
        VisibilityProperty.OverrideMetadata(typeof(ToolBarItem),
            new PropertyMetadata(Visibility.Visible, null, static (a, value) =>
                ((ToolBarItem)a).Form == ToolBarItemForm.Hidden ? Visibility.Collapsed : value));
    }

    private bool _onColumn;
    private double _columnWidth;

    /// <summary>Tells each of <paramref name="items"/>, the commands of one bar or section in their order, where it
    /// stands: on a column of <paramref name="columnWidth"/> when <paramref name="onColumn"/>, else along a bar. Each
    /// takes its form from that - a command that may fold, as it is measured, since only then is its width known. Whether
    /// any of them was moved, so is to be measured again.</summary>
    internal static bool Place(IReadOnlyList<ToolBarItem> items, bool onColumn, double columnWidth)
    {
        foreach (var item in items)
        {
            item._isLabel = false;
            item._labelSource = null;
        }

        if (onColumn)
        {
            // A label stands for the command it names: it goes with it, whatever the command's form.
            foreach (var item in items)
            {
                if (item.Content is not IAdamantiumComponent command ||
                    AutomationProperties.GetLabeledBy(command) is not { } label)
                {
                    continue;
                }

                foreach (var other in items)
                {
                    if (!ReferenceEquals(other, item) && ReferenceEquals(other.Content, label))
                    {
                        other._isLabel = true;
                        item._labelSource = label;
                        break;
                    }
                }
            }
        }

        var moved = false;
        foreach (var item in items)
        {
            moved |= item.Stand(onColumn, columnWidth);
        }

        return moved;
    }

    /// <summary>Takes its form again where it stands: its command's vertical form changed. A command that folds only when
    /// too wide is measured again for it.</summary>
    internal void Restand()
    {
        Stand(_onColumn, _columnWidth, true);
        InvalidateMeasure();
    }

    /// <summary>Its command's folded icon changed: the button shows it.</summary>
    internal void ShowFoldedIcon() => ShowLabel();

    private bool _placed;

    // Whether the item moved - to or from the column, or to a column of another width - or changed its form. Along a bar
    // as it was, nothing: a bar lays its commands out on every pass, most of them along it.
    private bool Stand(bool onColumn, double columnWidth, bool reform = false)
    {
        if (_placed && !onColumn && !_onColumn && !reform)
        {
            return false;
        }

        var moved = _onColumn != onColumn || onColumn && !_columnWidth.Equals(columnWidth);
        if (!_placed || _onColumn != onColumn)
        {
            SetValue(ToolBar.CommandOrientationProperty, onColumn ? Orientation.Vertical : Orientation.Horizontal);
        }

        _placed = true;
        _onColumn = onColumn;
        _columnWidth = columnWidth;
        FollowLabel(onColumn ? _labelSource : null);

        // What does not hang on the command's width is decided now: a hidden item is never measured to decide it. A
        // command that folds only when too wide is measured again when its column or its form changed - folded, its
        // width on the column cannot be told otherwise.
        var form = SetForm() ?? (Form == ToolBarItemForm.Hidden || (moved || reform) && Form == ToolBarItemForm.Folded
            ? ToolBarItemForm.Shown
            : Form);
        moved |= form != Form;
        Form = form;
        if (moved)
        {
            InvalidateMeasure();
        }

        return moved;
    }

    // The form that does not hang on the command's width; null, a command that folds only when too wide.
    private ToolBarItemForm? SetForm()
    {
        if (!_onColumn)
        {
            return ToolBarItemForm.Shown;
        }

        if (_isLabel)
        {
            return ToolBarItemForm.Hidden;
        }

        return (Content is IAdamantiumComponent command ? ToolBar.GetVerticalForm(command) : ToolBarVerticalForm.Auto) switch
        {
            ToolBarVerticalForm.Keep => ToolBarItemForm.Shown,
            ToolBarVerticalForm.Hide => ToolBarItemForm.Hidden,
            ToolBarVerticalForm.Fold => ToolBarItemForm.Folded,
            _ => null
        };
    }

    // A command that folds when too wide is measured as it stands on the column, in its own narrow form if it has one,
    // and folded if it is wider than the column. Folded stays folded while the column stays as it is: the command is on
    // its card then, drawn as along a bar, so its width on the column cannot be told again.
    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        if (_onColumn && Form == ToolBarItemForm.Shown && SetForm() == null && ContentHost is { } presenter)
        {
            presenter.Measure(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
            if (presenter.DesiredSize.Width > _columnWidth + Tolerance)
            {
                Form = ToolBarItemForm.Folded;
            }
        }

        return base.MeasureOverride(availableSize);
    }

    private const double Tolerance = 1e-6;

    // The label's text, followed: a language switched while the bar is down the side reaches the tooltip and the card.
    // Text only - a label holding a control is read for its name, its control left where it is.
    private static readonly AdamantiumProperty LabelTextProperty = AdamantiumProperty.Register("LabelText",
        typeof(object), typeof(ToolBarItem),
        new PropertyMetadata(null, static (a, e) => ((ToolBarItem)a).Label = e.NewValue switch
        {
            null => null,
            string text => text,
            UIComponent shown => shown.GetAutomationPeer()?.Name is { Length: > 0 } name ? name : null,
            var other => other.ToString()
        }));

    private IUIComponent _followed;

    private void FollowLabel(IUIComponent label)
    {
        if (ReferenceEquals(label, _followed))
        {
            return;
        }

        _followed = label;
        // The text left from a label no longer followed would stand over the binding.
        ClearValue(LabelTextProperty);
        switch (label)
        {
            case TextBlock:
                SetBinding(LabelTextProperty, new Binding(nameof(TextBlock.Text)) { Source = label });
                break;
            case ContentControl:
                SetBinding(LabelTextProperty, new Binding(nameof(Content)) { Source = label });
                break;
            default:
                RemoveBinding(LabelTextProperty);
                SetValue(LabelTextProperty, null);
                break;
        }
    }

    private void OnLabelChanged()
    {
        // The command's tooltip, unless it has one: the label is not beside it any more.
        var command = Content as AdamantiumComponent;
        var ownTip = command != null && ToolTipService.GetToolTip(command) != null;
        if (Label != null && !ownTip)
        {
            _tipFromLabel = true;
            SetValue(ToolTipService.ToolTipProperty, Label);
        }
        else if (_tipFromLabel)
        {
            _tipFromLabel = false;
            ClearValue(ToolTipService.ToolTipProperty);
        }

        ShowLabel();
    }

    // The fold button and the card's header say the command's label.
    private void ShowLabel()
    {
        if (_foldButton != null)
        {
            _foldButton.Header = LabelOrName();
            _foldButton.Content = Face();
        }

        if (_cardHeader != null)
        {
            _cardHeader.Content = LabelOrName();
        }
    }

    private void OnFormChanged()
    {
        CoerceValue(VisibilityProperty);
        Host();
        // A card asked open before the command folded opens with the fold; unfolded, there is no card.
        if (Form == ToolBarItemForm.Folded)
        {
            OnIsCardOpenChanged();
        }
        else
        {
            SetCurrentValue(IsCardOpenProperty, false);
        }
    }

    // The command where its form shows it: in the item, or - folded - on the card beside the column, the button in its
    // place. Let go of before it is taken, so it is never in two places; until the card is first built, a folded
    // command is in neither, as a collapsed ribbon group's is until its flyout opens.
    private void Host()
    {
        if (ContentHost is not { } presenter)
        {
            return;
        }

        if (Form == ToolBarItemForm.Folded)
        {
            if (_foldButton == null)
            {
                _foldButton = new ToolBarFoldButton();
                _foldButton.Click += OnFoldButtonClick;
            }

            if (!ReferenceEquals(presenter.Content, _foldButton))
            {
                presenter.ContentTemplate = null;
                presenter.Content = _foldButton;
            }

            if (_cardPresenter != null && !ReferenceEquals(_cardPresenter.Content, Content))
            {
                _cardPresenter.ContentTemplate = ContentTemplate;
                _cardPresenter.Content = Content;
            }

            ShowLabel();
            return;
        }

        if (_cardPresenter != null)
        {
            _cardPresenter.Content = null;
        }

        if (!ReferenceEquals(presenter.Content, Content) || !ReferenceEquals(presenter.ContentTemplate, ContentTemplate))
        {
            presenter.Content = null;
            presenter.ContentTemplate = ContentTemplate;
            presenter.Content = Content;
        }

        // The presenter discards the button it lets go of - a discard is one way - so the next fold makes a new one.
        if (_foldButton != null)
        {
            _foldButton.Click -= OnFoldButtonClick;
            _foldButton = null;
        }
    }

    // Its label, else what automation would call it.
    private object LabelOrName() =>
        Label ?? (Content is UIComponent command && command.GetAutomationPeer()?.Name is { Length: > 0 } name ? name : null);

    // Its icon if the application gives one, else the first two letters of its label.
    private object Face()
    {
        if (Content is IAdamantiumComponent command && ToolBar.GetFoldedIcon(command) is { } icon)
        {
            return icon;
        }

        return LabelOrName() is string text ? Initials(text) : null;
    }

    internal static string Initials(string text)
    {
        var start = 0;
        while (start < text.Length && !char.IsLetterOrDigit(text[start]))
        {
            start++;
        }

        if (start == text.Length)
        {
            return null;
        }

        var first = char.ToUpperInvariant(text[start]).ToString();
        return start + 1 < text.Length && char.IsLetterOrDigit(text[start + 1]) ? first + text[start + 1] : first;
    }
}
