using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.AST.TypeReference;

namespace Adamantium.UI.Markup.AST;

/// <summary>What <c>{Localize Table.Key}</c> becomes once its table is found: a string of a language table, and the
/// bindings that fill its placeholders.</summary>
public class AumlAstLocalizedStringNode : AumlAstNode, IAumlAstValueNode
{
    public AumlAstLocalizedStringNode(IAumlLineInfo info, string tableFullName, string key,
        IReadOnlyList<IAumlAstMarkupExtensionArgument> arguments, IAumlAstValueNode keySource = null)
        : base(info)
    {
        TableFullName = tableFullName;
        Key = key;
        Arguments = arguments;
        KeySource = keySource;
    }

    public IAumlAstTypeReference TypeReference { get; set; }

    /// <summary>The table's full CLR name.</summary>
    public string TableFullName { get; }

    /// <summary>The string's key: a property of the table, or a method when the string has placeholders. Null when the
    /// key is read from <see cref="KeySource"/>.</summary>
    public string Key { get; }

    /// <summary>The binding the key is read from, <c>{Localize CanvasStrings, Key={Binding Sort}}</c>; null for a key
    /// written out.</summary>
    public IAumlAstValueNode KeySource { get; }

    /// <summary>The named arguments that fill the placeholders, in the order they were written.</summary>
    public IReadOnlyList<IAumlAstMarkupExtensionArgument> Arguments { get; }

    public override IAumlAstNode Clone(AumlAstObjectNode parent) =>
        new AumlAstLocalizedStringNode(this, TableFullName, Key, Arguments, KeySource);
}
