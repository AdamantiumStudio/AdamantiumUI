using System;

namespace Adamantium.UI.Core;

internal class ValueContainer
{
    // The number of priority slots, resolved ONCE. Was Enum.GetValues<ValuePriority>() PER container (i.e. per property
    // per component) - a reflection call run tens of thousands of times when a virtualized list realizes a burst of
    // items. Now a plain fixed-size array indexed by (int)priority.
    private static readonly int SlotCount = Enum.GetValues<ValuePriority>().Length;

    // Slots are allocated on the first write; most properties are never written, and the default lives in a field.
    private object[] _values;

    private object _defaultValue = AdamantiumProperty.UnsetValue;

    // The effective value, computed on write so a read is one lock-free volatile read; reads vastly outnumber writes.
    private volatile object _effective = AdamantiumProperty.UnsetValue;

    // The winning value before coercion, kept so it can be re-coerced when its constraints change; guarded by the
    // container's lock.
    private object _base = AdamantiumProperty.UnsetValue;

    // The lowest-priority SOURCE slot; everything below it is the computed tail (see ValuePriority).
    private const int LastSourcePriority = (int)ValuePriority.Default;

    // Which slot the effective value currently comes from. Kept so the common Set does NOT rescan: writing a value at or
    // above the winning priority simply becomes the new winner. Only a write that could UNCOVER a lower slot (clearing
    // the winner, or writing below it) has to look, and those are rare.
    private int _effectiveFrom = LastSourcePriority + 1;

    /// <summary>Writes a slot and returns the new BASE value - the winning slot's request, still uncoerced. The caller
    /// coerces it and hands the result back through <see cref="SetEffective"/>; until then the effective value is
    /// unchanged, so a reader never sees a value that has skipped its coercion.</summary>
    public object SetValue(object value, ValuePriority priority)
    {
        var slot = (int)priority;

        // Still array-free: a seeded default stays in its field, anything else brings the slots into being.
        if (_values == null)
        {
            if (slot == LastSourcePriority)
            {
                _defaultValue = value;
                _effectiveFrom = LastSourcePriority;
                return _base = value;
            }

            Materialize();
        }

        _values[slot] = value;

        if (value != AdamantiumProperty.UnsetValue && slot <= _effectiveFrom)
        {
            _effectiveFrom = slot;
            return _base = value;
        }

        if (slot > _effectiveFrom) return _base;   // written under the winner - it changes nothing that is visible

        _effectiveFrom = LastSourcePriority + 1;
        return _base = Scan();
    }

    // Give the container its real slots, carrying the seeded default across. Runs at most once, and only for a property
    // something actually writes.
    private void Materialize()
    {
        var values = new object[SlotCount];
        Array.Fill(values, AdamantiumProperty.UnsetValue);
        values[LastSourcePriority] = _defaultValue;
        _values = values;
    }

    public object GetValue(ValuePriority priority)
    {
        if (_values != null) return _values[(int)priority];

        return (int)priority == LastSourcePriority ? _defaultValue : AdamantiumProperty.UnsetValue;
    }

    public bool IsDefaultOnly => _effectiveFrom >= (int)ValuePriority.Inherited;

    /// <summary>Which inheritance EPOCH the cached inherited value was resolved in (see
    /// <see cref="AdamantiumProperty.InheritanceEpoch"/>). -1 = never resolved. A stamp per container, so one element
    /// re-resolving costs nothing to the rest.</summary>
    public long InheritedStamp { get; set; } = -1;

    /// <summary>Caches an inherited value resolved from an ancestor, without notifying. Writes are ordered so a racing
    /// reader sees the value before or after, never half of it.</summary>
    public void SetInheritedCache(object raw, object coerced, long epoch)
    {
        if (raw == AdamantiumProperty.UnsetValue)
        {
            InheritedStamp = epoch;   // no ancestor holds one, so the default stands and there is nothing to cache
            return;
        }

        if (_values == null) Materialize();

        if ((int)ValuePriority.Inherited > _effectiveFrom)
        {
            // Something above the inherited slot wins anyway: keep the slot for completeness and stamp it resolved.
            _values[(int)ValuePriority.Inherited] = raw;
            InheritedStamp = epoch;
            return;
        }

        _values[(int)ValuePriority.Inherited] = raw;
        _base = raw;
        _effectiveFrom = (int)ValuePriority.Inherited;
        _effective = coerced;   // published LAST: this is the one a lock-free reader actually reads
        InheritedStamp = epoch;
    }

    /// <summary>The effective value: the winning slot's request, coerced. A plain field read - no scan, no bookkeeping,
    /// nothing to synchronise.</summary>
    public object Effective => _effective;

    /// <summary>What the winning slot asked for, before coercion - the input a re-coercion works from.</summary>
    public object BaseValue => _base;

    /// <summary>Publishes the coerced value. ONE reference write: a reader sees the value from before this update or the
    /// one after it, never a container caught mid-change.</summary>
    public void SetEffective(object value) => _effective = value;

    private object Scan()
    {
        if (_values == null)
        {
            _effectiveFrom = LastSourcePriority;
            return _defaultValue;
        }

        for (var i = 0; i <= LastSourcePriority; i++)
        {
            if (_values[i] == AdamantiumProperty.UnsetValue) continue;

            _effectiveFrom = i;
            return _values[i];
        }

        return AdamantiumProperty.UnsetValue;
    }
}
