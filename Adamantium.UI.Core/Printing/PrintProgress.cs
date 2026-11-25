namespace Adamantium.UI.Core.Printing;

/// <summary>How far printing has got: <see cref="Sheet"/> of <see cref="Sheets"/> sent.</summary>
public readonly record struct PrintProgress(int Sheet, int Sheets);
