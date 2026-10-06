using Adamantium.Fonts.Shaping;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// Typographic choices by name, on any element and inherited by the text inside it:
/// <c>Typography.Capitals="SmallCaps"</c>, <c>Typography.NumeralAlignment="Tabular"</c>. Each one is a font feature;
/// <see cref="FeaturesOf"/> lists them, and an element's own <c>FontFeatures</c> come after them and win.
/// </summary>
public static class Typography
{
    public static readonly AdamantiumProperty LigaturesProperty = Register("Ligatures", FontLigatures.Default);

    public static readonly AdamantiumProperty CapitalsProperty = Register("Capitals", FontCapitals.Normal);

    public static readonly AdamantiumProperty NumeralStyleProperty = Register("NumeralStyle", FontNumeralStyle.Normal);

    public static readonly AdamantiumProperty NumeralAlignmentProperty =
        Register("NumeralAlignment", FontNumeralAlignment.Normal);

    public static readonly AdamantiumProperty FractionProperty = Register("Fraction", FontFraction.Normal);

    public static readonly AdamantiumProperty VariantsProperty = Register("Variants", FontVariants.Normal);

    public static FontLigatures GetLigatures(IAdamantiumComponent element) =>
        element.GetValue<FontLigatures>(LigaturesProperty);

    public static void SetLigatures(IAdamantiumComponent element, FontLigatures value) =>
        element.SetValue(LigaturesProperty, value);

    public static FontCapitals GetCapitals(IAdamantiumComponent element) =>
        element.GetValue<FontCapitals>(CapitalsProperty);

    public static void SetCapitals(IAdamantiumComponent element, FontCapitals value) =>
        element.SetValue(CapitalsProperty, value);

    public static FontNumeralStyle GetNumeralStyle(IAdamantiumComponent element) =>
        element.GetValue<FontNumeralStyle>(NumeralStyleProperty);

    public static void SetNumeralStyle(IAdamantiumComponent element, FontNumeralStyle value) =>
        element.SetValue(NumeralStyleProperty, value);

    public static FontNumeralAlignment GetNumeralAlignment(IAdamantiumComponent element) =>
        element.GetValue<FontNumeralAlignment>(NumeralAlignmentProperty);

    public static void SetNumeralAlignment(IAdamantiumComponent element, FontNumeralAlignment value) =>
        element.SetValue(NumeralAlignmentProperty, value);

    public static FontFraction GetFraction(IAdamantiumComponent element) =>
        element.GetValue<FontFraction>(FractionProperty);

    public static void SetFraction(IAdamantiumComponent element, FontFraction value) =>
        element.SetValue(FractionProperty, value);

    public static FontVariants GetVariants(IAdamantiumComponent element) =>
        element.GetValue<FontVariants>(VariantsProperty);

    public static void SetVariants(IAdamantiumComponent element, FontVariants value) =>
        element.SetValue(VariantsProperty, value);

    /// <summary>The features the element's typographic choices ask for, followed by <paramref name="own"/>; null when
    /// there are none.</summary>
    public static IReadOnlyList<FontFeature> FeaturesOf(IAdamantiumComponent element, IReadOnlyList<FontFeature> own)
    {
        var features = new List<FontFeature>();
        var ligatures = GetLigatures(element);
        if ((ligatures & FontLigatures.Standard) == 0)
        {
            features.Add(FontFeature.Ligatures.Off);
        }

        if ((ligatures & FontLigatures.Contextual) == 0)
        {
            features.Add(FontFeature.Ligatures.ContextualOff);
        }

        if ((ligatures & FontLigatures.Discretionary) != 0)
        {
            features.Add(FontFeature.Ligatures.Discretionary);
        }

        if ((ligatures & FontLigatures.Historical) != 0)
        {
            features.Add(FontFeature.Ligatures.Historical);
        }

        AddCapitals(features, GetCapitals(element));
        switch (GetNumeralStyle(element))
        {
            case FontNumeralStyle.Lining:
                features.Add(FontFeature.Numerals.Lining);
                break;
            case FontNumeralStyle.Oldstyle:
                features.Add(FontFeature.Numerals.Oldstyle);
                break;
        }

        switch (GetNumeralAlignment(element))
        {
            case FontNumeralAlignment.Proportional:
                features.Add(FontFeature.Numerals.Proportional);
                break;
            case FontNumeralAlignment.Tabular:
                features.Add(FontFeature.Numerals.Tabular);
                break;
        }

        switch (GetFraction(element))
        {
            case FontFraction.Slashed:
                features.Add(FontFeature.Numerals.Fractions);
                break;
            case FontFraction.Stacked:
                features.Add(FontFeature.Numerals.StackedFractions);
                break;
        }

        AddVariants(features, GetVariants(element));
        if (own != null)
        {
            features.AddRange(own);
        }

        return features.Count == 0 ? null : features;
    }

    private static AdamantiumProperty Register<T>(string name, T defaultValue)
    {
        return AdamantiumProperty.RegisterAttached(name, typeof(T), typeof(AdamantiumComponent),
            new PropertyMetadata(defaultValue, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.AffectsMeasure));
    }

    private static void AddCapitals(List<FontFeature> features, FontCapitals capitals)
    {
        switch (capitals)
        {
            case FontCapitals.SmallCaps:
                features.Add(FontFeature.Capitals.Small);
                break;
            case FontCapitals.AllSmallCaps:
                features.Add(FontFeature.Capitals.Small);
                features.Add(FontFeature.Capitals.SmallFromCapitals);
                break;
            case FontCapitals.PetiteCaps:
                features.Add(FontFeature.Capitals.Petite);
                break;
            case FontCapitals.AllPetiteCaps:
                features.Add(FontFeature.Capitals.Petite);
                features.Add(FontFeature.Capitals.PetiteFromCapitals);
                break;
            case FontCapitals.Unicase:
                features.Add(FontFeature.Capitals.Unicase);
                break;
            case FontCapitals.Titling:
                features.Add(FontFeature.Capitals.Titling);
                break;
        }
    }

    private static void AddVariants(List<FontFeature> features, FontVariants variants)
    {
        switch (variants)
        {
            case FontVariants.Superscript:
                features.Add(FontFeature.Position.Superscript);
                break;
            case FontVariants.Subscript:
                features.Add(FontFeature.Position.Subscript);
                break;
            case FontVariants.Ordinal:
                features.Add(FontFeature.Numerals.Ordinal);
                break;
            case FontVariants.Inferior:
                features.Add(FontFeature.Position.ScientificInferior);
                break;
        }
    }
}
