namespace Adamantium.UI.LanguageServer;

/// <summary>A replacement of the text between two offsets.</summary>
public sealed record AumlFormatEdit(int Start, int End, string NewText);
