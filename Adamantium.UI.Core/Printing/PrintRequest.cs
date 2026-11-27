namespace Adamantium.UI.Core.Printing;

/// <summary>What to print and how, as chosen on a print page shown inside a window: the document, the settings, and the
/// page that was current - for <see cref="PageRange.Current"/>, from 0.</summary>
public sealed record PrintRequest(IPrintDocument Document, PrintSettings Settings, int CurrentPage = 0);
