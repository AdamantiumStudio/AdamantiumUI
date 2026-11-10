using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>A group begun, inside <see cref="Depth"/> others.</summary>
internal sealed record OpenedGroup(DrawingGroupOptions Options, int Depth);
