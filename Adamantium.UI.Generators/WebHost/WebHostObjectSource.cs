using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Generators.WebHost;

/// <summary>What one interface marked <c>[WebHostObject]</c> makes: its files, or why it cannot be made. Equal to
/// another that makes the same, so an unchanged interface is not made again.</summary>
internal sealed class WebHostObjectSource : IEquatable<WebHostObjectSource>
{
    public WebHostObjectSource(IReadOnlyList<(string HintName, string Source)> files, IReadOnlyList<Diagnostic> diagnostics)
    {
        Files = files;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<(string HintName, string Source)> Files { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool Equals(WebHostObjectSource other) =>
        other != null && Files.SequenceEqual(other.Files) &&
        Diagnostics.Select(d => d.ToString()).SequenceEqual(other.Diagnostics.Select(d => d.ToString()));

    public override bool Equals(object obj) => Equals(obj as WebHostObjectSource);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var (hintName, source) in Files)
        {
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(hintName);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(source);
        }

        return hash * 31 + Diagnostics.Count;
    }
}
