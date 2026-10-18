namespace Adamantium.UI.Markup.AST;

public class AumlAstPropertyNode : AumlAstNode
{
    public AumlAstPropertyNode(
        IAumlLineInfo info, 
        IAumlAstNode property, 
        IAumlAstValueNode value) : base(info)
    {
        Property = property;
        Values = [value];
    }
    
    public AumlAstPropertyNode(
        IAumlLineInfo info, 
        IAumlAstNode property, 
        IEnumerable<IAumlAstValueNode> values) : base(info)
    {
        Property = property;
        Values = values.ToList();
    }
    
    public IAumlAstNode Property { get; set; }

    public List<IAumlAstValueNode> Values { get; set; }

    /// <summary>Whether the markup sets the property with a property element (<c>&lt;Button.Content&gt;</c>), not an
    /// attribute or what the transformer makes.</summary>
    public bool IsElement { get; set; }

    public override IAumlAstNode Clone(AumlAstObjectNode parent) =>
        new AumlAstPropertyNode(this, Property?.Clone(parent),
            Values.Select(value => (IAumlAstValueNode)value.Clone(parent))) { IsElement = IsElement };

    public override string ToString()
    {
        return $"{Property}, Values: {Values.Count}";
    }
}