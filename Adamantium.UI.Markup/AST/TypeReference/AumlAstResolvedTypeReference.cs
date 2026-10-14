namespace Adamantium.UI.Markup.AST.TypeReference;

public class AumlAstResolvedTypeReference : AumlAstClrTypeReference
{
    public AumlAstResolvedTypeReference(IAumlLineInfo info, string @namespace, string name, string assembly, bool isMarkupExtension) : base(info, @namespace, name, assembly)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentNullException(nameof(name));
        }
        if (string.IsNullOrEmpty(@namespace))
        {
            throw new ArgumentNullException(nameof(@namespace));
        }
        if (string.IsNullOrEmpty(assembly))
        {
            throw new ArgumentNullException(nameof(assembly));
        }
        
        IsMarkupExtension = isMarkupExtension;
    }

    /// <param name="fullTypeName">The type's full name when the caller has it already: kept only if it is exactly the
    /// name <see cref="AumlAstClrTypeReference.GetFullTypeName"/> would make.</param>
    public AumlAstResolvedTypeReference(IAumlLineInfo info, string @namespace, string name, string assembly, bool isMarkupExtension,
        string fullTypeName) : this(info, @namespace, name, assembly, isMarkupExtension)
    {
        if (fullTypeName != null
            && fullTypeName.Length == @namespace.Length + 1 + name.Length
            && fullTypeName[@namespace.Length] == '.'
            && fullTypeName.StartsWith(@namespace, StringComparison.Ordinal)
            && fullTypeName.EndsWith(name, StringComparison.Ordinal))
        {
            KnowFullTypeName(fullTypeName);
        }
    }

    public override bool IsResolved => true;
}