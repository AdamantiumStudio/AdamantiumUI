using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core;

// Per-property stack of trigger contributions ordered by ISetter.DeclarationOrder, so leaving one trigger restores the one
// beneath.
internal class TriggerValueContainer
{
    private readonly List<(object Token, object Value, long Order)> _values = [];

    /// <summary>Records a trigger setter's contribution. Re-applying an existing token updates its value IN PLACE, so an
    /// idempotent re-apply or a {ThemeResource} refresh changes nothing about who wins.</summary>
    public void Set(object token, object value)
    {
        var order = token is ISetter s ? (long)s.StyleBand * 1_000_000 + s.DeclarationOrder : 0L;

        for (var i = 0; i < _values.Count; i++)
        {
            if (ReferenceEquals(_values[i].Token, token))
            {
                _values[i] = (token, value, order);
                return;
            }
        }

        _values.Add((token, value, order));
    }

    public void Remove(object token)
    {
        for (var i = 0; i < _values.Count; i++)
        {
            if (ReferenceEquals(_values[i].Token, token))
            {
                _values.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>The value that wins right now: the one written LOWEST in the markup among those still applied, or
    /// <see cref="AdamantiumProperty.UnsetValue"/> when the stack is empty (so the slot falls through below Trigger).
    /// Ties - contributions with no declared order, e.g. a code-made setter - keep the last-applied rule.</summary>
    public object EffectiveValue
    {
        get
        {
            if (_values.Count == 0) return AdamantiumProperty.UnsetValue;

            var winner = 0;
            for (var i = 1; i < _values.Count; i++)
            {
                if (_values[i].Order >= _values[winner].Order) winner = i;
            }

            return _values[winner].Value;
        }
    }
}
