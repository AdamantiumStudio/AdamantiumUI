namespace Adamantium.UI.LanguageServer;

/// <summary>How markup is laid out: the indent of one level, and whether the file is a language file, whose phrases
/// keep their attributes on one line.</summary>
public sealed record AumlFormatOptions(int TabSize = 4, bool InsertSpaces = true, bool IsLanguageFile = false);
