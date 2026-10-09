using System.Collections.Generic;
using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.Markup.AST;

/// <summary>One AUML <c>x:</c> directive: its local name, the description tooling shows, where it may be written, and
/// whether its value names a CLR type (so tooling completes type names in the value - both the plain
/// <c>prefix:Type</c> form and inside <c>{x:Type ...}</c>). See <see cref="AumlDirectives.All"/>.</summary>
public sealed class AumlDirectiveInfo
{
    public AumlDirectiveInfo(string name, bool isTypeReference = false,
        AumlDirectiveUsage usage = AumlDirectiveUsage.Attribute, IReadOnlyList<string> values = null)
    {
        Name = name;
        IsTypeReference = isTypeReference;
        Usage = usage;
        Values = values ?? [];
    }

    public string Name { get; }

    /// <summary>What the directive does, in the language of the current UI culture.</summary>
    public string Description => MarkupMessages.Directive(Name);

    public bool IsTypeReference { get; }

    public AumlDirectiveUsage Usage { get; }

    /// <summary>The words the value may be, for a directive that takes one of a fixed few; empty otherwise.</summary>
    public IReadOnlyList<string> Values { get; }
}
