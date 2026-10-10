using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

/// <summary>One group a <see cref="RenderCache"/> drew, as a diagnostic reads it: whose it is, where it is in logical window
/// units, what clips it, and how much of the retained batches it holds.</summary>
internal readonly record struct GroupView(
    IUIComponent Component,
    Rect World,
    Rect? Clip,
    float Opacity,
    int Units,
    int Slots,
    bool Current);
