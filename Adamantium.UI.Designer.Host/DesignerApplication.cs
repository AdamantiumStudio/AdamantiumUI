using Adamantium.UI.Universes;

namespace Adamantium.UI.Designer.Host;

/// <summary>
/// The application the designer boots when the previewed project declares none of its own (a library of views); a
/// project that does is previewed as that application, with its services. Never <c>Run()</c>: no main loop and no native
/// window. A <see cref="MultiverseApplication"/> so a design-time preview can still host a universe, driven by the
/// session (one snapshot of N frames per preview) rather than by a live loop.
/// </summary>
public class DesignerApplication : MultiverseApplication
{
}
