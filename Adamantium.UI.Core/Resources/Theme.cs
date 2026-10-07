using System.Collections.Specialized;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Resources;

public class Theme : AdamantiumComponent, ITheme
{
    public Theme()
    {
        StyleSets = new StyleSetCollection();
        StyleIncludes = new StyleIncludeCollection();
        Variants = new ThemeVariantCollection();
        // Declared through ONE path whether it came from markup or from code: the collection is what markup fills, and
        // adding to it is what creates the palette brushes. A theme file and a hand-built theme must not differ here.
        Variants.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (ThemeVariantDefinition definition in e.NewItems) AddVariant(definition);
        };
        ResourceManager = UIAppContext.Current.ResourceManager;
        MergedStyles = new StyleSet();
        MergedStyles.Styles.CollectionChanged += (_, _) => _typeStyleCache.Clear();
        // Seed the theme's font so it's a real (non-null) property value from the start: a {ThemeResource FontFamily}
        // binding reads the raw GetValue, and a theme can override it (live) to restyle all text.
        FontFamily = SystemDefaultFontFamily;
    }

    public Theme(string name) : this()
    {
        Name = name;
    }

    public string Name { get; protected set; }

    // The ONE accent seed. Setting it derives the whole ramp below (Default/hover/pressed + the on-accent text color),
    // so a theme - or a runtime accent swap - specifies a single color and every accented control stays correct and
    // readable. This is the piece WinUI/Avalonia leave to fixed per-theme tokens (which break on a custom accent).
    public static readonly AdamantiumProperty AccentColorProperty = AdamantiumProperty.Register(
        nameof(AccentColor), typeof(Brush), typeof(Theme), new PropertyMetadata(null, OnAccentColorChanged));

    public Brush AccentColor
    {
        get => GetValue<Brush>(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    private static void OnAccentColorChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not Theme theme) return;

        if (e.NewValue is SolidColorBrush seed)
        {
            theme.DeriveAccentPalette(seed.Color);
        }
        else if (e.NewValue is Brush brush)
        {
            // A non-solid accent (gradient/image) has no single color to darken or measure for contrast. Use it flat
            // for all three fills (no hover/pressed ramp) and default the on-accent text to white - so a non-solid seed
            // degrades gracefully instead of leaving the ramp derived from a PREVIOUS solid seed. Themes use a solid seed.
            theme.AccentFillColorDefault = brush;
            theme.AccentFillColorSecondary = brush;
            theme.AccentFillColorTertiary = brush;
            theme.AccentForegroundColor = new SolidColorBrush(White);
        }
    }

    // How much darker the hover / pressed accents are than the seed (0..1, toward black). Theme-settable so the accent
    // ramp can be tuned per theme; defaults match Fluent's feel. Changing one re-derives the ramp from the current seed.
    public static readonly AdamantiumProperty AccentHoverDarkenProperty = AdamantiumProperty.Register(
        nameof(AccentHoverDarken), typeof(double), typeof(Theme), new PropertyMetadata(0.12, OnAccentRampChanged));

    public static readonly AdamantiumProperty AccentPressedDarkenProperty = AdamantiumProperty.Register(
        nameof(AccentPressedDarken), typeof(double), typeof(Theme), new PropertyMetadata(0.24, OnAccentRampChanged));

    /// <summary>How opaque the SELECTION wash is (0..1). Theme-settable, like the darken factors above.</summary>
    public static readonly AdamantiumProperty AccentSelectionOpacityProperty = AdamantiumProperty.Register(
        nameof(AccentSelectionOpacity), typeof(double), typeof(Theme), new PropertyMetadata(0.4, OnAccentRampChanged));

    public double AccentSelectionOpacity
    {
        get => GetValue<double>(AccentSelectionOpacityProperty);
        set => SetValue(AccentSelectionOpacityProperty, value);
    }

    private static Color WithAlpha(Color color, double opacity) =>
        new(color.R, color.G, color.B, (byte)Math.Clamp(opacity * 255.0, 0, 255));

    /// <summary>Fraction (0..1, toward black) the hover accent (<see cref="AccentFillColorSecondary"/>) is darkened from
    /// the seed. Default 0.12.</summary>
    public double AccentHoverDarken
    {
        get => GetValue<double>(AccentHoverDarkenProperty);
        set => SetValue(AccentHoverDarkenProperty, value);
    }

    /// <summary>Fraction (0..1, toward black) the pressed accent (<see cref="AccentFillColorTertiary"/>) is darkened from
    /// the seed. Default 0.24.</summary>
    public double AccentPressedDarken
    {
        get => GetValue<double>(AccentPressedDarkenProperty);
        set => SetValue(AccentPressedDarkenProperty, value);
    }

    private static void OnAccentRampChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        // A ramp coefficient changed: re-derive from the current seed so hover/pressed pick up the new factor.
        if (a is Theme { AccentColor: SolidColorBrush seed } theme)
            theme.DeriveAccentPalette(seed.Color);
    }

    // One seed -> the ramp: hover/pressed a notch darker (by the AccentHover/PressedDarken factors), and the on-accent
    // TEXT chosen for contrast against the FILL (white on a dark accent, black on a light one) so a checked control reads
    // for ANY accent. Disabled/focus stay theme-authored (they're neutral, not accent-derived).
    private void DeriveAccentPalette(Color seed)
    {
        Recolor(AccentFillColorDefaultProperty, seed);
        Recolor(AccentFillColorSecondaryProperty, Color.Lerp(seed, Black, (float)AccentHoverDarken));   // hover
        Recolor(AccentFillColorTertiaryProperty, Color.Lerp(seed, Black, (float)AccentPressedDarken));  // pressed
        // Derived, not authored in a palette: they have to follow the seed like the rest of the ramp, or a runtime
        // accent change would leave every selected row in the previous color.
        Recolor(AccentFillColorSelectionProperty, WithAlpha(seed, AccentSelectionOpacity));
        Recolor(AccentFillColorSelectionStrongProperty, WithAlpha(seed, AccentSelectionOpacity + 0.15));
        Recolor(AccentForegroundColorProperty, OnAccent(seed));
    }

    // Recolor the existing brush instead of replacing it: consumers already hold it, so only a paint change travels, with
    // no property write per consumer.
    private void Recolor(AdamantiumProperty property, Color color)
    {
        if (GetValue(property) is SolidColorBrush brush)
        {
            // Marked here as well as where one is made: a seed assigned to the theme from markup or from a theme's own
            // constructor arrives as somebody else's brush and becomes the theme's the moment the ramp is derived from
            // it. Missing that, the accent - the brush the most things in an application hold - would be the one brush
            // an editor was still free to write into.
            brush.IsShared = true;
            brush.Color = color;
            return;
        }

        // First time, or a theme that put something other than a solid brush there: there is no identity to keep yet.
        SetValue(property, Shared(new SolidColorBrush(color)));
    }

    private static readonly Color Black = Color.FromRgba(0, 0, 0);
    private static readonly Color White = Color.FromRgba(255, 255, 255);

    // Perceptual luminance (Rec. 601): white text on a dark fill, black on a light one.
    private static Color OnAccent(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B < 140 ? White : Black;

    private static FontFamily _systemDefaultFontFamily;

    /// <summary>The per-platform system UI font - the single place the platform font choice lives, used as the default
    /// for any theme that doesn't pick its own.</summary>
    public static FontFamily SystemDefaultFontFamily => _systemDefaultFontFamily ??= new FontFamily(
        OperatingSystem.IsWindows() ? "Segoe UI" : OperatingSystem.IsMacOS() ? "Helvetica" : "DejaVu Sans");

    // The font is the theme's runtime-mutable identity, like the accent brushes above: it's an AdamantiumProperty so a
    // change raises PropertyChanged and every consumer (a {ThemeResource FontFamily} binding in a style) refreshes live -
    // no theme reload.
    public static readonly AdamantiumProperty FontFamilyProperty = AdamantiumProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(Theme), new PropertyMetadata(null));

    /// <summary>The theme's font for text - consume it in styles via <c>{ThemeResource FontFamily}</c> (descendants also
    /// inherit it through UIComponent.FontFamily). Unset falls back to <see cref="SystemDefaultFontFamily"/>; changing it
    /// at runtime refreshes every consumer live (it's an observable AdamantiumProperty).</summary>
    public FontFamily FontFamily
    {
        get => GetValue<FontFamily>(FontFamilyProperty) ?? SystemDefaultFontFamily;
        set => SetValue(FontFamilyProperty, value);
    }

    // Accent/focus brushes are the theme's runtime-mutable identity: change one (theme.AccentFillColorDefault = ...)
    // and every {ThemeResource} binding refreshes live - no theme reload. The static palette stays in the brush
    // dictionary. Plain AdamantiumProperties; the binding engine observes their change notifications.
    public static readonly AdamantiumProperty AccentFillColorDefaultProperty = AdamantiumProperty.Register(
        nameof(AccentFillColorDefault), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty AccentFillColorSecondaryProperty = AdamantiumProperty.Register(
        nameof(AccentFillColorSecondary), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty AccentFillColorTertiaryProperty = AdamantiumProperty.Register(
        nameof(AccentFillColorTertiary), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty AccentFillColorSelectionProperty = AdamantiumProperty.Register(
        nameof(AccentFillColorSelection), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty AccentFillColorSelectionStrongProperty = AdamantiumProperty.Register(
        nameof(AccentFillColorSelectionStrong), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty AccentFillColorDisabledProperty = AdamantiumProperty.Register(
        nameof(AccentFillColorDisabled), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty AccentForegroundColorProperty = AdamantiumProperty.Register(
        nameof(AccentForegroundColor), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty FocusStrokeColorOuterProperty = AdamantiumProperty.Register(
        nameof(FocusStrokeColorOuter), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public static readonly AdamantiumProperty FocusStrokeColorInnerProperty = AdamantiumProperty.Register(
        nameof(FocusStrokeColorInner), typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    public Brush AccentFillColorDefault
    {
        get => GetValue<Brush>(AccentFillColorDefaultProperty);
        set => SetValue(AccentFillColorDefaultProperty, value);
    }

    public Brush AccentFillColorSecondary
    {
        get => GetValue<Brush>(AccentFillColorSecondaryProperty);
        set => SetValue(AccentFillColorSecondaryProperty, value);
    }

    public Brush AccentFillColorTertiary
    {
        get => GetValue<Brush>(AccentFillColorTertiaryProperty);
        set => SetValue(AccentFillColorTertiaryProperty, value);
    }

    public Brush AccentFillColorSelection
    {
        get => GetValue<Brush>(AccentFillColorSelectionProperty);
        set => SetValue(AccentFillColorSelectionProperty, value);
    }

    public Brush AccentFillColorSelectionStrong
    {
        get => GetValue<Brush>(AccentFillColorSelectionStrongProperty);
        set => SetValue(AccentFillColorSelectionStrongProperty, value);
    }

    public Brush AccentFillColorDisabled
    {
        get => GetValue<Brush>(AccentFillColorDisabledProperty);
        set => SetValue(AccentFillColorDisabledProperty, value);
    }

    public Brush AccentForegroundColor
    {
        get => GetValue<Brush>(AccentForegroundColorProperty);
        set => SetValue(AccentForegroundColorProperty, value);
    }

    public Brush FocusStrokeColorOuter
    {
        get => GetValue<Brush>(FocusStrokeColorOuterProperty);
        set => SetValue(FocusStrokeColorOuterProperty, value);
    }

    public Brush FocusStrokeColorInner
    {
        get => GetValue<Brush>(FocusStrokeColorInnerProperty);
        set => SetValue(FocusStrokeColorInnerProperty, value);
    }

    protected IResourceManager ResourceManager { get; }

    public StyleSet MergedStyles { get; }

    // Cache of the matched-and-ordered style set per runtime type, for components with no id and no classes (see below).
    // Concurrent because styling is no longer a one-thread affair: a subtree can be materialized off the loop thread
    // (deferred tab content) and a virtualizing panel rebinds its tiles across cores, so this memo is written from more
    // than one thread. The value is a pure function of the type, so two threads racing compute the same array.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Style[]> _typeStyleCache = new();

    public Style[] FindStylesForComponent(IFundamentalUIComponent component)
    {
        if (component == null) return [];

        var type = component.GetType();

        // Without id or classes the matched set depends only on the type, so cache it per type. An unset Id reads as "",
        // hence IsNullOrEmpty.
        var cacheable = string.IsNullOrEmpty(component.Id) && !component.HasClassNames;
        if (cacheable && _typeStyleCache.TryGetValue(type, out var cached)) return cached;

        // Keep type styles only from the nearest styled ancestor type (a subclass without its own style falls back to its
        // base); typeless selectors always apply. Order is base-first and stable.
        var matched = MergedStyles.Styles.Where(x => x.Selector.Match(component)).ToArray();
        var nearestDistance = matched
            .Where(x => x.Selector.Types.Count > 0)
            .Select(x => x.Selector.SpecificityDistance(type))
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        var styles = matched
            .Where(x => x.Selector.Types.Count == 0 || x.Selector.SpecificityDistance(type) == nearestDistance)
            .OrderByDescending(x => x.Selector.SpecificityDistance(type))
            .ToArray();

        if (cacheable)
            _typeStyleCache[type] = styles;
        return styles;
    }

    // Expand a BasedOn selector into the base styles a deriving style pulls in - base-first, deduped, and recursive (a
    // base may itself be BasedOn another). `seen` starts with the deriving style so a base can't re-pull it (cycle guard).
    internal void CollectBasedOn(StyleSelector basedOn, List<Style> result, HashSet<Style> seen)
    {
        foreach (var type in basedOn.Types)
            foreach (var s in StylesForType(type))
                AddWithBases(s, result, seen);
    }

    private void AddWithBases(Style s, List<Style> result, HashSet<Style> seen)
    {
        if (!seen.Add(s)) return;
        if (s.BasedOn is { Types.Count: > 0 }) CollectBasedOn(s.BasedOn, result, seen);   // its own bases first
        result.Add(s);
    }

    // The PURE-TYPE styles for a type (no id/class/group facet) - the set an instance of exactly that type would match.
    // BasedOn pulls these in for a derived control that opts into the base look.
    private IEnumerable<Style> StylesForType(Type type) =>
        MergedStyles.Styles.Where(s =>
            s.Selector.Id == null && s.Selector.Classes.Count == 0 && s.Selector.ClassGroups.Count == 0
            && s.Selector.Types.Any(t => t == type));

    // ── Variants ──────────────────────────────────────────────────────────────────────────────────────────────────
    // The theme owns one brush per palette key; a variant supplies colors, so switching writes O(palette keys) and no
    // element.

    private readonly Dictionary<string, SolidColorBrush> _palette = new();
    private readonly Dictionary<ThemeVariant, ThemeVariantDefinition> _variants = new();

    /// <summary>What markup writes: <c>&lt;Theme.Variants&gt;…&lt;/Theme.Variants&gt;</c>. Adding to it declares the
    /// variant, so a theme file and a theme built in code go through exactly the same path.</summary>
    public ThemeVariantCollection Variants { get; }

    /// <summary>The theme's palette brushes by key - created once from the variants' color tables and never replaced.
    /// Their IDENTITY is what makes a variant switch cheap.</summary>
    public IReadOnlyDictionary<string, SolidColorBrush> Palette => _palette;

    public IReadOnlyDictionary<ThemeVariant, ThemeVariantDefinition> VariantsByKey => _variants;

    /// <summary>The variant in force; it changes - and says so, as a property of the theme - once the palette and the
    /// theme's values are all in the new variant.</summary>
    public static readonly AdamantiumProperty CurrentVariantProperty = AdamantiumProperty.Register(
        nameof(CurrentVariant), typeof(ThemeVariant), typeof(Theme), new PropertyMetadata(default(ThemeVariant)));

    public ThemeVariant CurrentVariant
    {
        get => GetValue<ThemeVariant>(CurrentVariantProperty);
        private set => SetValue(CurrentVariantProperty, value);
    }

    /// <summary>The variant used when nothing else is said - the first one declared.</summary>
    public ThemeVariant DefaultVariant { get; private set; }

    public ThemeVariant SystemLightVariant { get; set; }

    public ThemeVariant SystemDarkVariant { get; set; }

    /// <summary>Declare a variant. The palette gains a brush for any color key it has not seen yet, so the brushes
    /// exist before anything asks for them and never have to be swapped later.</summary>
    public void AddVariant(ThemeVariantDefinition variant)
    {
        if (variant == null || variant.Key.IsUnspecified) return;

        _variants[variant.Key] = variant;
        if (DefaultVariant.IsUnspecified) DefaultVariant = variant.Key;

        RegisterPaletteKeys(variant);

        // ...and keep registering. Markup adds the variant to the theme BEFORE filling in its colors - the loader
        // parents a child and then populates it - so a palette built once, here, would come out empty for every theme
        // read from a file while looking perfectly correct for every theme built in a test. The keys arrive when they
        // arrive; this listens rather than assuming an order.
        variant.Colors.CollectionChanged += (_, _) => RegisterPaletteKeys(variant);
    }

    // Keys served as a raw Color rather than as a brush - a gradient STOP takes a color. Kept beside the brushes
    // rather than in a second collection on the variant: one palette, two ways of being asked for.
    private readonly Dictionary<string, Color> _rawColors = new();

    /// <summary>Palette entries a variant declares as colors rather than brushes.</summary>
    public IReadOnlyDictionary<string, Color> RawColors => _rawColors;

    private void RegisterPaletteKeys(ThemeVariantDefinition variant)
    {
        foreach (var entry in variant.Colors)
        {
            if (entry.Key == null) continue;

            if (entry.As == PaletteEntryKind.Color)
            {
                if (!_rawColors.ContainsKey(entry.Key)) _rawColors[entry.Key] = entry.Color;
                continue;
            }

            if (_palette.ContainsKey(entry.Key)) continue;

            // The brush is created with the color of whichever variant is CURRENT, when that variant declares the key
            // - so a palette entry is never briefly the wrong color on its way to being right.
            var color = entry.Color;
            if (!CurrentVariant.IsUnspecified && _variants.TryGetValue(CurrentVariant, out var current)
                && current.Colors.TryGet(entry.Key, out var currentColor))
            {
                color = currentColor;
            }

            _palette[entry.Key] = Shared(new SolidColorBrush(color));
        }
    }

    // MARKED as the theme's on the way out, at each of the three places one is made. Everything that receives a theme
    // brush receives the same object, so an editor that writes into it recolors the application - see Brush.IsShared.
    private static SolidColorBrush Shared(SolidColorBrush brush)
    {
        brush.IsShared = true;
        return brush;
    }

    /// <summary>Every variant of a theme must answer the SAME set of keys. A key one variant declares and another does
    /// not would leave the palette holding whatever the previous variant put there - so the subtree's appearance would
    /// depend on which variant it was switched FROM, which is not a thing anyone can reason about. Returns the keys
    /// that are missing somewhere, by variant; empty means the theme is consistent.</summary>
    public IReadOnlyList<string> ValidateVariants()
    {
        var problems = new List<string>();
        if (_variants.Count < 2) return problems;

        foreach (var variant in _variants.Values)
        {
            foreach (var key in _palette.Keys)
            {
                if (!variant.Colors.ContainsKey(key)) problems.Add($"{variant.Key}: no color for '{key}'");
            }
        }

        return problems;
    }

    public bool ApplyVariant(ThemeVariant variant)
    {
        if (variant.FollowsSystem) return false;   // the caller resolves this one first - see ResolveSystemVariant
        if (variant.IsUnspecified) variant = DefaultVariant;
        if (variant.IsUnspecified || !_variants.TryGetValue(variant, out var definition)) return false;

        // Write colors into the existing brushes. Raw color entries are values nobody holds, so their change must be
        // announced.
        var rawChanged = false;

        foreach (var entry in definition.Colors)
        {
            if (entry.Key == null) continue;

            if (entry.As == PaletteEntryKind.Color)
            {
                if (!_rawColors.TryGetValue(entry.Key, out var had) || had != entry.Color) rawChanged = true;
                _rawColors[entry.Key] = entry.Color;
                continue;
            }

            if (_palette.TryGetValue(entry.Key, out var brush)) brush.Color = entry.Color;
            else _palette[entry.Key] = Shared(new SolidColorBrush(entry.Color));
        }

        // ONCE, after the whole palette is in place, and only when something actually moved. Per key would re-resolve
        // every live reference in the application once per color, which is the shape of fan-out that has frozen this
        // loop before; and announcing mid-way would hand a listener a palette half in one variant and half in the other.
        if (rawChanged) ResourceManager?.NotifyResourcesChanged();

        // ...then the theme's own properties, which is where {ThemeResource} looks. AccentColor derives the whole ramp
        // on assignment, so setting the seed is enough.
        foreach (var entry in definition.Values)
        {
            if (entry.Property == null) continue;
            var property = AdamantiumPropertyMap.FindRegistered(GetType(), entry.Property);
            if (property == null) continue;

            if (entry.Value is Brush handed) handed.IsShared = true;
            SetValue(property, entry.Value);
        }

        CurrentVariant = variant;
        return true;
    }

    // ── One theme, several variants AT ONCE ───────────────────────────────────────────────────────────────────────
    // A subtree pinned to another variant gets a sibling theme sharing the Style objects but with its own palette; it
    // pays one re-style when it opts in.

    private readonly Dictionary<ThemeVariant, Theme> _siblings = new();
    private Theme _variantRoot;   // the theme this one was made from; null on the original

    /// <summary>The theme object that shows <paramref name="variant"/> - this one when it already does, otherwise a
    /// sibling sharing every style with it. Returns this theme unchanged when the variant is not one it declares:
    /// giving back something else would be the silent substitution <see cref="ApplyVariant"/> refuses to make.</summary>
    public Theme SiblingForVariant(ThemeVariant variant)
    {
        if (variant.IsUnspecified || variant.FollowsSystem) return this;
        if (!_variants.ContainsKey(variant)) return this;

        // Always a sibling, even for the current variant: sharing the app's brushes would let an app-wide switch repaint
        // the pinned subtree.

        var root = _variantRoot ?? this;
        lock (root._siblings)
        {
            if (root._siblings.TryGetValue(variant, out var existing)) return existing;

            var sibling = new Theme(Name) { _variantRoot = root, FontFamily = FontFamily };
            foreach (var styleSet in StyleSets) sibling.StyleSets.Add(styleSet);
            sibling.Merge(MergedStyles.Styles);

            // The definitions are DATA and are shared: a variant's color table is read, never written, and having two
            // copies drift apart would be a bug nobody could see.
            foreach (var definition in _variants.Values) sibling.AddVariant(definition);

            sibling.ApplyVariant(variant);
            root._siblings[variant] = sibling;
            return sibling;
        }
    }

    /// <summary>The theme this one is a variant sibling of - itself when it is the original.</summary>
    public Theme VariantRoot => _variantRoot ?? this;

    public ThemeVariant ResolveSystemVariant(bool osPrefersDark)
    {
        var wanted = osPrefersDark ? SystemDarkVariant : SystemLightVariant;
        return wanted.IsUnspecified || !_variants.ContainsKey(wanted) ? default : wanted;
    }

    /// <summary>The palette's answer for <paramref name="key"/>, or null. Brushes first, then the few keys a variant
    /// declares as raw colors.</summary>
    internal object PaletteValue(string key)
    {
        if (_palette.TryGetValue(key, out var brush)) return brush;
        return _rawColors.TryGetValue(key, out var color) ? color : null;
    }

    public object GetResource(string key)
    {
        return PaletteValue(key) ?? ResourceManager.FindResource(key);
    }

    public bool TryGetResource(string key, out object value)
    {
        value = PaletteValue(key) ?? ResourceManager.FindResource(key);
        return value != null;
    }

    // The requester-aware pair asks the tree-scoped chain FIRST and the palette last: a Local dictionary on the
    // requester's own subtree is meant to override the theme, and answering from the palette before looking would make
    // a theme key impossible to shadow locally.
    public object GetResource(IFundamentalUIComponent requester, string key)
    {
        return ResourceManager.FindResource(requester, key) ?? PaletteValue(key);
    }

    public bool TryGetResource(IFundamentalUIComponent requester, string key, out object value)
    {
        value = GetResource(requester, key);
        return value != null;
    }

    public StyleSetCollection StyleSets { get; }

    public StyleIncludeCollection StyleIncludes { get; }

    public void AddStyleSet(StyleSet styleSet)
    {
        styleSet.Initialize(this);
        StyleSets.Add(styleSet);
        Merge(styleSet.Styles);

        lock (_siblings)
        {
            foreach (var sibling in _siblings.Values)
            {
                sibling.StyleSets.Add(styleSet);
                sibling.Merge(styleSet.Styles);
            }
        }
    }

    /// <summary>Takes styles into this theme, stamping each as the THEME'S - which is what stops it outranking the
    /// application's own styles for the same type (see <see cref="Style.Band"/>).</summary>
    private void Merge(IEnumerable<Style> styles)
    {
        var taken = styles as IList<Style> ?? styles.ToList();
        foreach (var style in taken) style?.MarkThemeOwned();
        MergedStyles.AddStyles(taken);
    }

    public void Initialize()
    {
        if (Initialized || Initializing) return;
        Initializing = true;

        // A theme with variants must apply one before use: accent and focus properties are set only by ApplyVariant.
        if (CurrentVariant.IsUnspecified && !DefaultVariant.IsUnspecified) ApplyVariant(DefaultVariant);

        foreach (var styleInclude in StyleIncludes)
        {
            var styleSet = (StyleSet)Activator.CreateInstance(styleInclude.Source);
            styleSet?.Initialize(this);
            StyleSets.Add(styleSet);
        }

        var styles = StyleSets.SelectMany(x => x.Styles).ToList();
        Merge(styles);
        StyleSets.CollectionChanged += OnStyleSetRepositoriesChanged;
        Initialized = true;
        Initializing = false;
    }

    public bool Initialized { get; private set; }

    public bool Initializing { get; private set; }

    private void OnStyleSetRepositoriesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {

    }
}