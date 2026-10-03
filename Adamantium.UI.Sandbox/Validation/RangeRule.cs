using System;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.Validation;

/// <summary>A number this page will accept only between two limits. The limits are BOUND from the view-model rather
/// than written into the markup, which is the whole point of a rule being a component: what a page considers acceptable
/// is a page's business and can change while it is open.</summary>
public class RangeRule : DataGridValidationRule
{
    public static readonly AdamantiumProperty MinProperty = AdamantiumProperty.Register(nameof(Min),
        typeof(int), typeof(RangeRule), new PropertyMetadata(int.MinValue));

    public static readonly AdamantiumProperty MaxProperty = AdamantiumProperty.Register(nameof(Max),
        typeof(int), typeof(RangeRule), new PropertyMetadata(int.MaxValue));

    public int Min
    {
        get => GetValue<int>(MinProperty);
        set => SetValue(MinProperty, value);
    }

    public int Max
    {
        get => GetValue<int>(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    public override string Validate(object value, object item)
    {
        if (value == null) return null;

        int number;
        try
        {
            number = Convert.ToInt32(value);
        }
        catch (Exception)
        {
            return GridPageStrings.NotANumber(value);
        }

        if (number < Min) return GridPageStrings.BelowFloor(number, Min);
        if (number > Max) return GridPageStrings.OverCeiling(number, Max);
        return null;
    }
}
