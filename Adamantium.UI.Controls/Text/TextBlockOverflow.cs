using System;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>Where the text of a <see cref="TextBlock"/> that does not fit goes on, as WinUI's RichTextBlockOverflow and
/// InDesign's threaded frames: the block's <see cref="TextBlock.OverflowContentTarget"/> names it, and its own
/// <see cref="OverflowContentTarget"/> names the next. The text is laid out once, by the block, through its room and
/// then through the room each of these gives, so a paragraph, its hyphenation and its baseline grid run on across them;
/// it is drawn, selected and clicked here as in the block, in the block's formatting. One block's text at a time: a
/// second block reaching it ends its chain before it. Collapsed, or out of the tree, it ends the chain.</summary>
public class TextBlockOverflow : InputUIComponent
{
    public static readonly AdamantiumProperty OverflowContentTargetProperty = AdamantiumProperty.Register(
        nameof(OverflowContentTarget), typeof(TextBlockOverflow), typeof(TextBlockOverflow),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, OnChainChanged));

    public static readonly AdamantiumProperty HasOverflowContentProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(HasOverflowContent), typeof(bool), typeof(TextBlockOverflow), new PropertyMetadata(false));

    private readonly BaselineGridFollower _grid;
    private Size _room = new(double.NaN, double.NaN);
    private Size _content;
    private int _revision = -1;

    // A block whose chain stopped here while another block's text was shown: it takes this one when that block lets go.
    private WeakReference<TextBlock> _waiting;

    public TextBlockOverflow()
    {
        _grid = new BaselineGridFollower(this, () => null, () => 0, Regrid);
    }

    /// <summary>Where the text goes on past this one; null where it ends here.</summary>
    public TextBlockOverflow OverflowContentTarget
    {
        get => GetValue<TextBlockOverflow>(OverflowContentTargetProperty);
        set => SetValue(OverflowContentTargetProperty, value);
    }

    /// <summary>Whether the text goes on past this one.</summary>
    public bool HasOverflowContent
    {
        get => GetValue<bool>(HasOverflowContentProperty);
        internal set => SetValue(HasOverflowContentProperty, value);
    }

    /// <summary>The block whose text this shows; null while no block's chain reaches it.</summary>
    public TextBlock ContentSource { get; private set; }

    // The room this gives the text, in the block's units: what it was measured with.
    internal Size Room => _room;

    // The baseline grid this stands on, its rows measured from its top; null for none.
    internal BaselineGrid Grid => _grid.Grid();

    internal double GridPhase => Grid?.Offset ?? double.NaN;

    // Where its part of the block's layout stands, in the layout's units; null while it shows none.
    internal RectangleF? Frame { get; private set; }

    // Whether the block may run its text on into this: shown, in the tree, and no other block's text here.
    internal bool Join(TextBlock source)
    {
        if (ContentSource != null && !ReferenceEquals(ContentSource, source))
        {
            _waiting = new WeakReference<TextBlock>(source);
            return false;
        }

        if (!IsAttachedToVisualTree || Visibility == Visibility.Collapsed)
        {
            _waiting = new WeakReference<TextBlock>(source);
            return false;
        }

        if (ContentSource == null)
        {
            ContentSource = source;
            InvalidateMeasure();
            InvalidateRender(false);
        }

        return true;
    }

    internal void Leave(TextBlock source)
    {
        if (!ReferenceEquals(ContentSource, source))
        {
            return;
        }

        ContentSource = null;
        Show(null, default, false, -1);
        if (_waiting != null && _waiting.TryGetTarget(out var waiting))
        {
            _waiting = null;
            waiting.InvalidateMeasure();
        }
    }

    // The part of the layout it shows, as tall as its lines; what changed is measured and drawn again.
    internal void Show(RectangleF? frame, Size content, bool more, int revision)
    {
        HasOverflowContent = more;
        if (frame != Frame || content != _content)
        {
            Frame = frame;
            _content = content;
            InvalidateMeasure();
            InvalidateRender(false);
        }
        else if (revision != _revision)
        {
            InvalidateRender(false);
        }

        _revision = revision;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _grid.Measure();
        var room = new Size(double.IsNaN(Width) ? availableSize.Width : Width,
            double.IsNaN(Height) ? availableSize.Height : Height);
        if (!room.Equals(_room))
        {
            _room = room;
            ContentSource?.InvalidateMeasure();
        }

        return Frame == null ? new Size(0, 0) : _content;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ContentSource?.OverflowArranged();
        return finalSize;
    }

    protected override void OnRender(IDrawingContext context)
    {
        if (ContentSource is { } source && Frame is { } frame)
        {
            source.RenderFrame(context.ForControl(this), frame, RenderSize);
        }
    }

    // A control among the block's inlines shown here is the block's child: a press on it is the control's.
    public override bool HitTestCore(Vector2 localPoint) =>
        ContentSource is not { } source || Frame is not { } frame || !source.ShowsObjectAt(frame, localPoint);

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == VisibilityProperty)
        {
            Rechain();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _grid.Follow(attached: true);
        Rechain();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _grid.Follow(attached: false);
        Rechain();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        ContentSource?.OverflowPointerEntered(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ContentSource?.OverflowPointerMoved(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ContentSource?.OverflowPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ContentSource?.OverflowPointerReleased(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ContentSource?.OverflowPointerExited(e);
    }

    private void Regrid()
    {
        InvalidateMeasure();
        ContentSource?.InvalidateMeasure();
    }

    // Shown, hidden, put in or out of the tree: the block lays its chain out again.
    private void Rechain()
    {
        ContentSource?.InvalidateMeasure();
        if (_waiting != null && _waiting.TryGetTarget(out var waiting))
        {
            waiting.InvalidateMeasure();
        }
    }

    private static void OnChainChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is TextBlockOverflow overflow)
        {
            overflow.Rechain();
        }
    }
}
