namespace Adamantium.UI.LanguageServer;

/// <summary>A language table <c>{Localize}</c> can name: the class the build made of its language files.</summary>
public sealed record LanguageTableInfo(string Name, string FullName, IReadOnlyList<LanguageStringInfo> Strings);
