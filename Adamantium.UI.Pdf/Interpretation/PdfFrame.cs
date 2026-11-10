using System.Collections.Generic;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>A content stream being walked: its operations, the resources it names, where its draws go, and what to undo
/// when it ends - the states it saved, the groups it opened.</summary>
internal sealed class PdfFrame
{
    public PdfFrame(PdfFrameKind kind, IEnumerator<PdfContentOperation> operations, PdfResources resources,
        List<PdfDrawStep> output, PdfMatrix baseCtm, int floor, int groupsAtStart, PdfStream form)
    {
        Kind = kind;
        Operations = operations;
        Resources = resources;
        Output = output;
        BaseCtm = baseCtm;
        Floor = floor;
        GroupsAtStart = groupsAtStart;
        Form = form;
    }

    public PdfFrameKind Kind { get; }

    public IEnumerator<PdfContentOperation> Operations { get; }

    public PdfResources Resources { get; }

    /// <summary>The list the frame's draws are written to: the page's, or a mask's or a pattern cell's own.</summary>
    public List<PdfDrawStep> Output { get; }

    /// <summary>The matrix in force when the stream began: where its patterns' space starts.</summary>
    public PdfMatrix BaseCtm { get; }

    /// <summary>How many states were saved when the frame began: it brings back no state saved before it.</summary>
    public int Floor { get; }

    /// <summary>How many groups were open when the frame began; the ones it opened since are closed when it ends.
    /// </summary>
    public int GroupsAtStart { get; }

    /// <summary>The form being drawn, so that it is not entered again while it is; null for any other stream.</summary>
    public PdfStream Form { get; }

    /// <summary>Whether the frame opened a group of its own on entry - a form's transparency group, or the group a blend
    /// mode or soft mask wraps it in - closed after everything inside.</summary>
    public int OwnGroups { get; set; }

    /// <summary>The Type 3 font whose glyph this frame draws; its resources stand in when the glyph's have none.</summary>
    public PdfType3Font GlyphFont { get; init; }

    /// <summary>What to do with <see cref="Output"/> when the frame ends: for a soft mask or a pattern cell, keep it as a
    /// picture.</summary>
    public System.Action<List<PdfDrawStep>> Finish { get; init; }
}
