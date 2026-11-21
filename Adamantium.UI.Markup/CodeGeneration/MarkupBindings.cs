using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>What markup writes as a binding - one answer for the transformer, which judges where one may stand, and the
/// generator, which builds it.</summary>
internal static class MarkupBindings
{
    /// <summary>A binding written in braces - <c>{Binding}</c>, <c>{MultiBinding}</c>, and <c>{Ancestor}</c> and
    /// <c>{Self}</c>, which are bindings too - or as an element, <c>&lt;Binding/&gt;</c> or
    /// <c>&lt;MultiBinding&gt;</c>. Told by name, as a <c>{ResourceReference}</c> is.</summary>
    internal static bool IsBinding(IAumlAstNode node) => node switch
    {
        AumlAstMarkupExtensionNode extension => extension.TypeReference?.Name is "Binding" or "MultiBinding" or "Ancestor" or "Self",
        AumlAstObjectNode element => element.TypeReference?.Name is "Binding" or "MultiBinding",
        _ => false,
    };
}
