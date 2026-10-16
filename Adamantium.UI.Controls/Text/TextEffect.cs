using System;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>A range of a text drawn apart from the rest, as WPF's text effect: its characters moved by a
/// <see cref="Transform"/> and drawn in their own <see cref="Foreground"/>. Only the drawing changes: the lines, the caret,
/// selection and hit testing stay where the layout put them. It is a logical child of its text block or inline, so it
/// binds against their data. Name the transform to animate it:
/// <c>&lt;TextEffect PositionStart="0" PositionCount="1"&gt;&lt;TextEffect.Transform&gt;&lt;Transform
/// x:Name="First"/&gt;&lt;/TextEffect.Transform&gt;&lt;/TextEffect&gt;</c>.</summary>
public sealed class TextEffect : FundamentalUIComponent, ITransformHolder
{
    private Transform _transform;
    private Brush _bakedBrush;
    private bool _inBlock;

    public static readonly AdamantiumProperty PositionStartProperty = AdamantiumProperty.Register(nameof(PositionStart),
        typeof(int), typeof(TextEffect), new PropertyMetadata(0));

    public static readonly AdamantiumProperty PositionCountProperty = AdamantiumProperty.Register(nameof(PositionCount),
        typeof(int), typeof(TextEffect), new PropertyMetadata(0));

    public static readonly AdamantiumProperty TransformProperty = AdamantiumProperty.Register(nameof(Transform),
        typeof(Transform), typeof(TextEffect), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ForegroundProperty = AdamantiumProperty.Register(nameof(Foreground),
        typeof(Brush), typeof(TextEffect), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TransformOriginProperty = AdamantiumProperty.Register(
        nameof(TransformOrigin), typeof(TextEffectOrigin), typeof(TextEffect), new PropertyMetadata(TextEffectOrigin.Glyph));

    /// <summary>The first character of the range: in a text block, of its text, where a line break and a paragraph's end
    /// count one each; in an inline, of the inline's own text.</summary>
    public int PositionStart
    {
        get => GetValue<int>(PositionStartProperty);
        set => SetValue(PositionStartProperty, value);
    }

    /// <summary>How many characters the range takes; none (the default) draws nothing apart.</summary>
    public int PositionCount
    {
        get => GetValue<int>(PositionCountProperty);
        set => SetValue(PositionCountProperty, value);
    }

    /// <summary>How the range is moved, turned, scaled or slanted, about <see cref="TransformOrigin"/>; null moves
    /// nothing. Its <c>ShiftX</c>/<c>ShiftY</c> count in sizes of each character, or of the element with
    /// <see cref="TextEffectOrigin.Element"/>; turns about the X and Y axes with a <c>Perspective</c> tip the characters
    /// toward or away from the viewer, as a flipping card.</summary>
    public Transform Transform
    {
        get => GetValue<Transform>(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    /// <summary>A solid color brush the range is drawn in, its opacity with it; null keeps the text's color. A glyph's
    /// own colors, such as an emoji's, stay.</summary>
    public Brush Foreground
    {
        get => GetValue<Brush>(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>What <see cref="Transform"/> turns and scales about: each character's own center (the default) or the
    /// element's space.</summary>
    public TextEffectOrigin TransformOrigin
    {
        get => GetValue<TextEffectOrigin>(TransformOriginProperty);
        set => SetValue(TransformOriginProperty, value);
    }

    internal event EventHandler Changed;

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TransformProperty)
        {
            Hold(Transform);
        }
        else if (e.Property == ForegroundProperty)
        {
            Bake(_inBlock && !IsDiscarded ? Foreground : null);
        }
        else if (e.Property == DataContextProperty && _transform != null)
        {
            BindingEngine.RefreshBindings(_transform);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal override void OnRenderValueChanged(object sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    void ITransformHolder.OnTransformChanged(Transform transform) => Changed?.Invoke(this, EventArgs.Empty);

    AdamantiumComponent ITransformHolder.ParentFor(Transform transform)
    {
        for (var node = LogicalParent; node != null; node = node.LogicalParent)
        {
            if (node is IUIComponent)
            {
                return this;
            }
        }

        return null;
    }

    internal void ReparentTransform() => _transform?.Reparent();

    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        Hold(null);
        Bake(null);
    }

    internal void SetInBlock(bool inBlock)
    {
        _inBlock = inBlock;
        Bake(_inBlock && !IsDiscarded ? Foreground : null);
    }

    private void Bake(Brush brush)
    {
        if (ReferenceEquals(_bakedBrush, brush))
        {
            return;
        }

        _bakedBrush?.ReleaseBaked();
        _bakedBrush = brush;
        _bakedBrush?.HoldBaked();
    }

    private void Hold(Transform transform)
    {
        var previous = _transform;
        _transform = transform;
        previous?.RemoveHolder(this);
        _transform?.AddHolder(this);
    }

    internal bool TryResolve(int offset, int limit, Vector2F area, Size elementSize, out GlyphEffect effect)
    {
        effect = default;
        var end = Math.Min((long)PositionStart + Math.Max(PositionCount, 0), limit);
        var start = Math.Max(PositionStart, 0);
        if (end <= start)
        {
            return false;
        }

        Vector4F? color = null;
        if (Foreground is SolidColorBrush brush)
        {
            var straight = brush.Color.ToVector4();
            straight.W *= (float)brush.Opacity;
            color = straight;
        }

        var matrix = Matrix3x3F.Identity;
        if (Transform?.Matrix is { } m)
        {
            matrix = new Matrix3x3F((float)m.M11, (float)m.M12, (float)m.M14, (float)m.M21, (float)m.M22, (float)m.M24,
                (float)m.M41, (float)m.M42, (float)m.M44);
        }

        var aroundGlyph = TransformOrigin == TextEffectOrigin.Glyph;
        var shift = Transform is { } transform ? new Vector2F((float)transform.ShiftX, (float)transform.ShiftY) : default;
        if (!aroundGlyph)
        {
            matrix *= Move(shift.X * (float)elementSize.Width, shift.Y * (float)elementSize.Height);
            matrix = Move(area.X, area.Y) * matrix * Move(-area.X, -area.Y);
            shift = default;
        }

        if (matrix.IsIdentity && color == null && shift == default)
        {
            return false;
        }

        effect = new GlyphEffect(offset + start, (int)(end - start), matrix, aroundGlyph, color, shift);
        return true;
    }

    private static Matrix3x3F Move(float x, float y) => new(1, 0, 0, 0, 1, 0, x, y, 1);
}
