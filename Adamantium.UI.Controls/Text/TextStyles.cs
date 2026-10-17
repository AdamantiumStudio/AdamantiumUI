using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>The formatting a named text style sets for the text it is set on, as InDesign's styles: what it leaves
/// unset it takes from the style it is based on, and what an inline sets itself wins over it. A style lives as a
/// resource; changing it lays out again every text it is set on.</summary>
public abstract class TextFormat : AdamantiumComponent
{
    public static readonly AdamantiumProperty FontSizeProperty = AdamantiumProperty.Register(nameof(FontSize),
        typeof(double), typeof(TextFormat), new PropertyMetadata(double.NaN));

    public static readonly AdamantiumProperty FontWeightProperty = AdamantiumProperty.Register(nameof(FontWeight),
        typeof(FontWeight?), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty FontStyleProperty = AdamantiumProperty.Register(nameof(FontStyle),
        typeof(FontStyle?), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty FontStretchProperty = AdamantiumProperty.Register(nameof(FontStretch),
        typeof(FontStretch?), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ForegroundProperty = AdamantiumProperty.Register(nameof(Foreground),
        typeof(Brush), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty BackgroundProperty = AdamantiumProperty.Register(nameof(Background),
        typeof(Brush), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TextDecorationsProperty = AdamantiumProperty.Register(
        nameof(TextDecorations), typeof(TextDecorations?), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TrackingProperty = AdamantiumProperty.Register(nameof(Tracking),
        typeof(double?), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty KerningProperty = AdamantiumProperty.Register(nameof(Kerning),
        typeof(TextKerning?), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty LanguageProperty = AdamantiumProperty.Register(nameof(Language),
        typeof(string), typeof(TextFormat), new PropertyMetadata(null));

    public static readonly AdamantiumProperty BaselineShiftProperty = AdamantiumProperty.Register(
        nameof(BaselineShift), typeof(double?), typeof(TextFormat), new PropertyMetadata(null));

    // Whoever lays out by this style: inlines, and the styles based on it. Held weakly, so a style kept in a theme
    // keeps none of them alive; written from any thread.
    private readonly ConditionalWeakTable<ITextStyleUser, object> _users = new();
    private readonly object _usersLock = new();

    /// <summary>The style's name, as the resource key it is kept under usually says.</summary>
    public string Name { get; set; }

    /// <summary>The text's size; NaN sets none.</summary>
    public double FontSize
    {
        get => GetValue<double>(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>The text's weight; null sets none.</summary>
    public FontWeight? FontWeight
    {
        get => GetValue<FontWeight?>(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>The text's slant; null sets none.</summary>
    public FontStyle? FontStyle
    {
        get => GetValue<FontStyle?>(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>The text's width; null sets none.</summary>
    public FontStretch? FontStretch
    {
        get => GetValue<FontStretch?>(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>The text's color; null sets none.</summary>
    public Brush Foreground
    {
        get => GetValue<Brush>(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>What is drawn behind the text; null sets none.</summary>
    public Brush Background
    {
        get => GetValue<Brush>(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>The lines through, under or over the text; null sets none.</summary>
    public TextDecorations? TextDecorations
    {
        get => GetValue<TextDecorations?>(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>Space after each character, in thousandths of an em; null sets none.</summary>
    public double? Tracking
    {
        get => GetValue<double?>(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    /// <summary>Where the space between two letters comes from; null sets none.</summary>
    public TextKerning? Kerning
    {
        get => GetValue<TextKerning?>(KerningProperty);
        set => SetValue(KerningProperty, value);
    }

    /// <summary>The text's language, as a BCP 47 tag; null sets none.</summary>
    public string Language
    {
        get => GetValue<string>(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <summary>How far the text is raised from the baseline; null sets none.</summary>
    public double? BaselineShift
    {
        get => GetValue<double?>(BaselineShiftProperty);
        set => SetValue(BaselineShiftProperty, value);
    }

    // The style this one is based on, as its BasedOn says.
    private protected abstract TextFormat BaseFormat { get; }

    // What this style or the ones it is based on set, for the block: the font to resolve, the brushes to hold and what
    // an inline's own defaults yield to. A chain set in a loop through markup ends where it comes round.
    internal FontWeight? ResolvedWeight() => Resolved(static format => format.FontWeight, static weight => weight != null);

    internal FontStyle? ResolvedStyle() => Resolved(static format => format.FontStyle, static style => style != null);

    internal FontStretch? ResolvedStretch() => Resolved(static format => format.FontStretch, static stretch => stretch != null);

    internal Brush ResolvedForeground() => Resolved(static format => format.Foreground, static brush => brush != null);

    internal Brush ResolvedBackground() => Resolved(static format => format.Background, static brush => brush != null);

    internal TextDecorations? ResolvedDecorations() =>
        Resolved(static format => format.TextDecorations, static decorations => decorations != null);

    internal double? ResolvedBaselineShift() =>
        Resolved(static format => format.BaselineShift, static shift => shift != null);

    private T Resolved<T>(Func<TextFormat, T> value, Func<T, bool> isSet)
    {
        for (var format = this; format != null; format = format.Base)
        {
            var found = value(format);
            if (isSet(found))
            {
                return found;
            }
        }

        return default;
    }

    // The style this one is based on, unless the chain from there comes back round to it: then, as for the engine's
    // style, none.
    internal TextFormat Base => BaseFormat is { } based && !based.Reaches(this) ? based : null;

    // How many styles a chain of BasedOn is followed through.
    internal const int MaxChain = 64;

    // The attributes of the text this style itself sets; the engine's style takes the rest from the one it is based
    // on, and the font is the block's to resolve.
    internal TextAttributes OwnTextAttributes() => new()
    {
        FontSize = double.IsNaN(FontSize) ? null : FontSize,
        Foreground = (Foreground as SolidColorBrush)?.Color,
        Background = (Background as SolidColorBrush)?.Color,
        Decorations = TextDecorations,
        Tracking = Tracking,
        Kerning = Kerning,
        Language = Language,
        BaselineShift = BaselineShift,
    };

    internal void Use(ITextStyleUser user)
    {
        lock (_usersLock)
        {
            _users.AddOrUpdate(user, null);
        }
    }

    internal void Unuse(ITextStyleUser user)
    {
        lock (_usersLock)
        {
            _users.Remove(user);
        }
    }

    // What lays out by this style lays out again, told outside the lock; once, when a chain set in a loop brings the
    // news back round.
    private protected void NotifyUsers()
    {
        if (Interlocked.Exchange(ref _notifying, 1) == 1)
        {
            return;
        }

        try
        {
            lock (_usersLock)
            {
                foreach (var (user, _) in _users)
                {
                    _told.Add(user);
                }
            }

            foreach (var user in _told)
            {
                user.TextStyleChanged();
            }
        }
        finally
        {
            _told.Clear();
            Volatile.Write(ref _notifying, 0);
        }
    }

    private readonly List<ITextStyleUser> _told = [];
    private int _notifying;

    // Whether basing a style on this one would bring the chain round to the style itself.
    internal bool Reaches(TextFormat style)
    {
        var format = this;
        for (var depth = 0; format != null && depth < MaxChain; depth++, format = format.BaseFormat)
        {
            if (ReferenceEquals(format, style))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        NotifyUsers();
    }
}

// What lays out by a text style, and hears when it changes.
internal interface ITextStyleUser
{
    void TextStyleChanged();
}

/// <summary>A named character style, as InDesign's: formatting a range of inlines takes over its paragraph's style
/// and under what the inlines set themselves.</summary>
public class CharacterStyle : TextFormat, ITextStyleUser
{
    public static readonly AdamantiumProperty BasedOnProperty = AdamantiumProperty.Register(nameof(BasedOn),
        typeof(CharacterStyle), typeof(CharacterStyle), new PropertyMetadata(null));

    /// <summary>The style this one starts from: it sets over that one. Never one based on this.</summary>
    public CharacterStyle BasedOn
    {
        get => GetValue<CharacterStyle>(BasedOnProperty);
        set
        {
            if (value != null && value.Reaches(this))
            {
                throw new ArgumentException("A style cannot be based on itself.", nameof(value));
            }

            SetValue(BasedOnProperty, value);
        }
    }

    private protected override TextFormat BaseFormat => BasedOn;

    void ITextStyleUser.TextStyleChanged() => NotifyUsers();

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == BasedOnProperty)
        {
            (e.OldValue as CharacterStyle)?.Unuse(this);
            (e.NewValue as CharacterStyle)?.Use(this);
        }

        base.OnPropertyChanged(e);
    }
}

/// <summary>A named paragraph style, as InDesign's: how a paragraph stands and the formatting its inlines take, under
/// their character styles and what they set themselves; what the paragraph sets itself wins over it.</summary>
public class ParagraphStyle : TextFormat, ITextStyleUser
{
    public static readonly AdamantiumProperty BasedOnProperty = AdamantiumProperty.Register(nameof(BasedOn),
        typeof(ParagraphStyle), typeof(ParagraphStyle), new PropertyMetadata(null));

    public static readonly AdamantiumProperty HorizontalTextAlignmentProperty = AdamantiumProperty.Register(
        nameof(HorizontalTextAlignment), typeof(HorizontalTextAlignment?), typeof(ParagraphStyle), new PropertyMetadata(null));

    public static readonly AdamantiumProperty MarginProperty = AdamantiumProperty.Register(nameof(Margin),
        typeof(Thickness?), typeof(ParagraphStyle), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TextIndentProperty = AdamantiumProperty.Register(nameof(TextIndent),
        typeof(double), typeof(ParagraphStyle), new PropertyMetadata(double.NaN));

    public static readonly AdamantiumProperty LineHeightProperty = AdamantiumProperty.Register(nameof(LineHeight),
        typeof(double), typeof(ParagraphStyle), new PropertyMetadata(double.NaN));

    /// <summary>The style this one starts from: it sets over that one. Never one based on this.</summary>
    public ParagraphStyle BasedOn
    {
        get => GetValue<ParagraphStyle>(BasedOnProperty);
        set
        {
            if (value != null && value.Reaches(this))
            {
                throw new ArgumentException("A style cannot be based on itself.", nameof(value));
            }

            SetValue(BasedOnProperty, value);
        }
    }

    /// <summary>How the paragraph's lines stand between its edges; null sets none.</summary>
    public HorizontalTextAlignment? HorizontalTextAlignment
    {
        get => GetValue<HorizontalTextAlignment?>(HorizontalTextAlignmentProperty);
        set => SetValue(HorizontalTextAlignmentProperty, value);
    }

    /// <summary>The paragraph's room, as <see cref="Paragraph.Margin"/>; null sets none.</summary>
    public Thickness? Margin
    {
        get => GetValue<Thickness?>(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>How much further its first line starts, as <see cref="Paragraph.TextIndent"/>; NaN sets none.</summary>
    public double TextIndent
    {
        get => GetValue<double>(TextIndentProperty);
        set => SetValue(TextIndentProperty, value);
    }

    /// <summary>The height of its lines, as <see cref="Paragraph.LineHeight"/>; NaN sets none.</summary>
    public double LineHeight
    {
        get => GetValue<double>(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    private protected override TextFormat BaseFormat => BasedOn;

    // How the paragraph stands by what this style itself sets.
    internal ParagraphAttributes OwnParagraphAttributes()
    {
        var attributes = new ParagraphAttributes { Alignment = HorizontalTextAlignment };
        if (Margin is { } margin)
        {
            attributes = attributes.With(new ParagraphAttributes
            {
                MarginLeft = margin.Left, MarginRight = margin.Right, SpaceBefore = margin.Top, SpaceAfter = margin.Bottom,
            });
        }

        if (!double.IsNaN(TextIndent))
        {
            attributes = attributes.With(new ParagraphAttributes { TextIndent = TextIndent });
        }

        if (!double.IsNaN(LineHeight))
        {
            attributes = attributes.With(new ParagraphAttributes { LineHeight = LineHeight });
        }

        return attributes;
    }

    void ITextStyleUser.TextStyleChanged() => NotifyUsers();

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == BasedOnProperty)
        {
            (e.OldValue as ParagraphStyle)?.Unuse(this);
            (e.NewValue as ParagraphStyle)?.Use(this);
        }

        base.OnPropertyChanged(e);
    }
}
