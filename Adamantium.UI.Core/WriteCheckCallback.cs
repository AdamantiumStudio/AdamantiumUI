namespace Adamantium.UI.Core;

/// <summary>Asked before <paramref name="value"/> is written to a property of <paramref name="a"/> - from code, markup,
/// a style, a binding or an animation alike, <see cref="AdamantiumProperty.UnsetValue"/> for a value cleared - and
/// throws when the component takes no such value now: the property is then left as it was, nothing of the refused
/// value kept.</summary>
public delegate void WriteCheckCallback(AdamantiumComponent a, object value);
