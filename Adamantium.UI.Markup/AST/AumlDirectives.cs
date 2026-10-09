using System.Collections.Generic;

namespace Adamantium.UI.Markup.AST;

/// <summary>The registry of AUML <c>x:</c> directive names and descriptions, used by the code generator and by tooling via
/// <see cref="All"/>; behavior lives in the transformer.</summary>
public static class AumlDirectives
{
    public const string Name = "Name";
    public const string Namespace = "Namespace";
    public const string Key = "Key";
    public const string Type = "Type";
    public const string ViewModel = "ViewModel";
    public const string CreateInDesignTime = "CreateInDesignTime";
    public const string Null = "Null";
    public const string Shared = "Shared";
    public const string Static = "Static";
    public const string DataType = "DataType";
    public const string KeepAlive = "KeepAlive";
    public const string Load = "Load";

    /// <summary>Every directive with the description tooling shows in completion and hover.</summary>
    public static readonly IReadOnlyList<AumlDirectiveInfo> All =
    [
        new AumlDirectiveInfo(Name),
        new AumlDirectiveInfo(Namespace),
        new AumlDirectiveInfo(Key),
        new AumlDirectiveInfo(Type, isTypeReference: true, usage: AumlDirectiveUsage.Value),
        new AumlDirectiveInfo(ViewModel, isTypeReference: true),
        new AumlDirectiveInfo(CreateInDesignTime, values: ["True", "False"]),
        new AumlDirectiveInfo(Null, usage: AumlDirectiveUsage.Value),
        new AumlDirectiveInfo(KeepAlive, values: ["Disabled", "Enabled", "Required"]),
        new AumlDirectiveInfo(Load, values: ["True", "False"]),
        new AumlDirectiveInfo(DataType, isTypeReference: true),
        new AumlDirectiveInfo(Static, isTypeReference: true, usage: AumlDirectiveUsage.Value),
        new AumlDirectiveInfo(Shared, values: ["True", "False"]),
    ];

    /// <summary>The directive with this local name, or null if the name is not a directive at all.</summary>
    public static AumlDirectiveInfo Find(string name)
    {
        foreach (var directive in All)
        {
            if (directive.Name == name)
            {
                return directive;
            }
        }

        return null;
    }
}
