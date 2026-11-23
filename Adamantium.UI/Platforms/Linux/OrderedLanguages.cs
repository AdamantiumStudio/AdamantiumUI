using System.Collections.Generic;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The user's languages ordered with the session's own first, kept with the input sources' list they were
/// ordered from, so a new list from the desktop is told from the one already ordered.</summary>
internal sealed record OrderedLanguages(IReadOnlyList<string> Sources, IReadOnlyList<string> Languages);
