namespace Adamantium.UI.Core;

/// <summary>An inherited value reaching an element: what the element read before it, what it inherited before and
/// inherits now - each an ancestor's explicit value, or UnsetValue for none - and the inheritance epoch the change began
/// in, which tells a value the element held from one a read took while the change was on its way.</summary>
internal readonly record struct InheritedChange(object ReadBefore, object Before, object After, long Epoch);
