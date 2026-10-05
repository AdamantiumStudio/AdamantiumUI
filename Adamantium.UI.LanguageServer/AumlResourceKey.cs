using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.LanguageServer;

/// <summary>A resource key a project can reach, and the type of what it holds; null when that type is not known.</summary>
public sealed record AumlResourceKey(string Key, IResolvedType ValueType);
