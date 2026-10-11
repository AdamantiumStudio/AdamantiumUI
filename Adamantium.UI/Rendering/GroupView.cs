using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

/// <summary>One group in a <see cref="RenderCache"/>'s paint order, as a diagnostic reads it.</summary>
internal readonly record struct GroupView(IUIComponent Component, int Units);
