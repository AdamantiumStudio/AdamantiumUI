using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Audio.Tests;

/// <summary>A player told it left the tree, and came back, as a window would tell it.</summary>
internal sealed class DetachablePlayer : AudioPlayer
{
    public void Detach() => OnDetachedFromVisualTree(new VisualTreeAttachmentEventArgs(null, this));

    public void Attach() => OnAttachedToVisualTree(new VisualTreeAttachmentEventArgs(null, this));
}
