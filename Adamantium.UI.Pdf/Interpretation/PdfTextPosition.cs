using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>Where text stands between <c>BT</c> and <c>ET</c>: the text and line matrices, and the glyphs a clipping
/// rendering mode has gathered so far.</summary>
internal sealed record PdfTextPosition(PdfMatrix Text, PdfMatrix Line, IReadOnlyList<PlacedGlyphRun> Clip);
