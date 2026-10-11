using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

/// <summary>One group in a <see cref="RenderCache"/>'s paint order, as a diagnostic reads it: its units, and how many
/// copies of its subtree it draws (RenderClones; 0 = drawn once, in place).</summary>
internal readonly record struct GroupView(IUIComponent Component, int Units, int Clones);
