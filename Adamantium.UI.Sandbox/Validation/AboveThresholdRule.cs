using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core;

namespace Adamantium.UI.Sandbox.Validation;

/// <summary>Flags values above a bound threshold with the "Warning" state; the theme decides its color.</summary>
public class AboveThresholdRule : DataGridStateRule
{
    public static readonly AdamantiumProperty ThresholdProperty = AdamantiumProperty.Register(nameof(Threshold),
        typeof(int), typeof(AboveThresholdRule), new PropertyMetadata(int.MaxValue));

    public int Threshold
    {
        get => GetValue<int>(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    public override object State(object value, object item) =>
        int.TryParse(value?.ToString(), out var number) && number > Threshold ? "Warning" : null;
}
