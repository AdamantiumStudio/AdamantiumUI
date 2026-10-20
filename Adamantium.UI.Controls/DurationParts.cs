using System;

namespace Adamantium.UI.Controls;

/// <summary>Which parts a <see cref="DurationField"/> shows; the largest one shown counts everything above it.</summary>
[Flags]
public enum DurationParts
{
    None = 0,
    Days = 1,
    Hours = 2,
    Minutes = 4,
    Seconds = 8,
    Milliseconds = 16,
}
