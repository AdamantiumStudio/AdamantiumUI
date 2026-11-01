using System;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UITests;

/// <summary>A border with an inheriting property of its own, whose changed-callback reports to <see cref="LevelChanged"/> -
/// as a control's own callback on an inherited value does.</summary>
public sealed class InheritingProbe : Border
{
    public static readonly AdamantiumProperty LevelProperty = AdamantiumProperty.Register(nameof(Level), typeof(int),
        typeof(InheritingProbe), new PropertyMetadata(0, PropertyMetadataOptions.Inherits,
            (component, e) => ((InheritingProbe)component).LevelChanged?.Invoke((InheritingProbe)component, e)));

    public event Action<InheritingProbe, AdamantiumPropertyChangedEventArgs> LevelChanged;

    public int Level
    {
        get => GetValue<int>(LevelProperty);
        set => SetValue(LevelProperty, value);
    }
}
