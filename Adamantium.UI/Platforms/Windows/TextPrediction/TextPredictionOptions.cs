using System;

namespace Adamantium.UI.Platforms.Windows.TextPrediction;

/// <summary>Which candidates Windows.Data.Text.TextPredictionGenerator gives: words a beginning goes on to, or what a
/// misspelled word was meant as.</summary>
[Flags]
internal enum TextPredictionOptions : uint
{
    None = 0,
    Predictions = 1,
    Corrections = 2,
}
