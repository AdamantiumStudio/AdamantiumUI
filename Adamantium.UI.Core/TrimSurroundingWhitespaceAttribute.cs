namespace Adamantium.UI.Core;

/// <summary>Marks an element whose neighboring white space in markup text is dropped, as WPF's: the line break and
/// indentation written around a <c>&lt;LineBreak/&gt;</c> do not become spaces at the ends of the lines.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TrimSurroundingWhitespaceAttribute : Attribute
{
}
