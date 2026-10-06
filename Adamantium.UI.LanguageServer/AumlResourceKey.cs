using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.LanguageServer;

/// <summary>A resource key a project can reach, and the type of what it holds; null when that type is not known. A key
/// a markup file declares carries where: the file and the zero-based line and character of the key; a key known only
/// from a built assembly has no file.</summary>
public sealed record AumlResourceKey(string Key, IResolvedType ValueType, string File = null, int Line = -1, int Character = -1);
