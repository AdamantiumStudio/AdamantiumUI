using System.Security.Cryptography;
using System.Text;

namespace Adamantium.UI.Markup;

/// <summary>The fingerprint of a markup text, the same at build time and at run time: generated code carries the one of
/// the text it was built from, so a running application can tell whether the file on disk is still that text.</summary>
public static class MarkupHash
{
    /// <summary>The fingerprint of <paramref name="text"/>.</summary>
    public static string Of(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return string.Concat(bytes.Take(16).Select(b => b.ToString("x2")));
    }
}
