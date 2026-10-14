using Adamantium.UI.Markup.AST.TypeReference;

namespace Adamantium.UI.Markup.AST;

public class AumlAstTemplateNode : AumlAstObjectNode
{
    public AumlAstTemplateNode(IAumlLineInfo info, IAumlAstTypeReference type) :
        base(info, type)
    {
    }

    public IAumlAstValueNode Ast { get; set; }

    public override IAumlAstNode Clone(AumlAstObjectNode parent)
    {
        var copy = new AumlAstTemplateNode(this, TypeReference)
        {
            Ast = (IAumlAstValueNode)Ast?.Clone(parent)
        };

        CopyBodyInto(copy);
        return copy;
    }
}