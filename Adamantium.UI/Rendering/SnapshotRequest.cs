using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Rendering;

namespace Adamantium.UI.Rendering;

/// <summary>A snapshot of a live element waiting for the loop: the whole of it at its window's density, or
/// <see cref="Region"/> of it, in its own units, at <see cref="Scale"/> device pixels to the unit, unfaded - as much of it
/// as <see cref="Content"/> says.</summary>
internal sealed record SnapshotRequest(IUIComponent Visual, Rect? Region, double Scale, SnapshotContent Content,
    Action<ImageSource> OnReady);
