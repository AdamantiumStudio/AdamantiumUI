using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core;

internal class StyleValueContainer
{
    private List<StyleValuePair> _values;
    private HashSet<Style> _styleHash;

    public StyleValueContainer()
    {
        _values = new List<StyleValuePair>();
        _styleHash = new HashSet<Style>();
    }

    /// <summary>Record one style's contribution, KEPT ORDERED BY SPECIFICITY - least specific first, so the last entry
    /// is the one in force. Ordering rather than appending is what stops the order styles are DECLARED in from deciding
    /// the outcome: a plain setter writes at one priority, so before this the last style applied simply won. Equal
    /// specificity keeps insertion order, as the web does.</summary>
    public void AddValue(Style style, object value)
    {
        // With BasedOn a style writes a property twice (base first); the later write replaces the recorded value.
        foreach (var recorded in _values)
        {
            if (recorded.Style != style) continue;
            recorded.Value = value;
            return;
        }

        if (_styleHash.Contains(style))
            return;

        var band = style?.Band ?? 0;
        var at = _values.Count;
        while (at > 0 && (_values[at - 1].Style?.Band ?? 0) > band) at--;

        _values.Insert(at, new StyleValuePair(style, value));
        _styleHash.Add(style);
    }

    /// <summary>The contribution in force: the most specific one recorded, or nothing at all.</summary>
    public object EffectiveValue =>
        _values.Count > 0 ? _values[^1].Value : AdamantiumProperty.UnsetValue;

    public Resources.Style EffectiveStyle => _values.Count > 0 ? _values[^1].Style : null;

    /// <summary>Removes one style's contribution and returns the most specific remaining one, whatever the removal
    /// order.</summary>
    public object RemoveAndGetEffectiveValue(Style style)
    {
        var entry = _values.FirstOrDefault(x => x.Style == style);
        if (entry == null) return AdamantiumProperty.UnsetValue;

        _values.Remove(entry);
        _styleHash.Remove(style);

        return _values.Count > 0 ? _values[^1].Value : AdamantiumProperty.UnsetValue;
    }

    public object GetValue(Style style)
    {
        var entry = _values.FirstOrDefault(x => x.Style == style);
        return entry != null ? entry.Value : AdamantiumProperty.UnsetValue;
    }
}