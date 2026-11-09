using System;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Rendering;

/// <summary>A screenshot asked of a window: answered by the first frame that applied record <see cref="After"/> of the
/// window's tree or a later one.</summary>
internal readonly record struct ScreenshotRequest(Action<BitmapSource> OnReady, long After);
