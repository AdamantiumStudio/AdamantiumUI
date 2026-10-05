namespace Adamantium.UI.Core.Resources;

public class StyleInclude : IInclude
{
    [TypeOf(typeof(StyleSet))]
    public Type Source { get; set; }
}