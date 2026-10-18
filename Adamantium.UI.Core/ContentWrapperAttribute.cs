namespace Adamantium.UI.Core;

/// <summary>Marks a collection that markup may write text into, as WPF does inlines: each piece of text among its items
/// becomes an item of <see cref="WrapperType"/>, the text set through that type's <see cref="ContentAttribute"/>
/// property. <c>&lt;TextBlock&gt;Hello &lt;Bold&gt;world&lt;/Bold&gt;&lt;/TextBlock&gt;</c> then holds two runs.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ContentWrapperAttribute : Attribute
{
    /// <summary>The item type a piece of text is wrapped in.</summary>
    public Type WrapperType { get; set; }

    /// <summary>The item type an element written among the items, and not one itself, is wrapped in, as WPF wraps a
    /// control among inlines in an <c>InlineUIContainer</c>: set through that type's <see cref="ContentAttribute"/>
    /// property when the element fits it. Null wraps none.</summary>
    public Type ElementWrapperType { get; set; }
}
