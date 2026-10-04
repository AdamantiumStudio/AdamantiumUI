namespace Adamantium.UI.Core.Automation;

/// <summary>An element holding a number between two limits, such as a slider or a progress bar.</summary>
public interface IRangeValueProvider
{
    double Value { get; }

    double Minimum { get; }

    double Maximum { get; }

    double SmallChange { get; }

    double LargeChange { get; }

    bool IsReadOnly { get; }

    /// <summary>Moves to <paramref name="value"/>, which must lie between the limits.</summary>
    void SetValue(double value);
}
