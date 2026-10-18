using Adamantium.UI.Markup.AST.TypeReference;
using Adamantium.UI.Markup.Parsers;

namespace Adamantium.UI.Markup.AST;

public class AumlAstTextNode : AumlAstNode, IAumlAstValueNode
{
    public string Text { get; set; }
    
    public AumlAstTextNode(IAumlLineInfo info, string text) : base(info)
    {
        Text = text;
        TypeReference = new AumlAstXmlTypeReference(info, AumlNamespaces.AumlControls, "String");
    }

    public IAumlAstTypeReference TypeReference { get; set; }

    /// <summary>Whether the text stands under <c>xml:space="preserve"</c>: its white space is kept as written.</summary>
    public bool PreservesSpace { get; set; }

    public override IAumlAstNode Clone(AumlAstObjectNode parent)
    {
        EnsureNotDerived(typeof(AumlAstTextNode));

        return new AumlAstTextNode(this, Text) { TypeReference = TypeReference, PreservesSpace = PreservesSpace };
    }

    public override string ToString()
    {
        return Text;
    }
}