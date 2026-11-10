using System;
using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

// Text (ISO 32000-2, 9.3 and 9.4). Each string shown becomes one run of glyphs placed by one matrix: the run's x is the
// pen along the baseline in units of the font size, y down, and the matrix brings it into the page through the text
// rendering matrix. A Type 3 font's glyphs are content streams, walked as frames of their own.
internal sealed partial class PdfPageInterpreter
{
    private readonly Dictionary<PdfFont, IFont> _programs = new(ReferenceEqualityComparer.Instance);
    private readonly List<PlacedGlyphRun> _textClip = [];
    private PdfMatrix _textMatrix = PdfMatrix.Identity;
    private PdfMatrix _lineMatrix = PdfMatrix.Identity;

    private bool TextOperator(PdfContentOperation operation, PdfFrame frame)
    {
        switch (operation.Operator)
        {
            case "BT":
                _textMatrix = _lineMatrix = PdfMatrix.Identity;
                _textClip.Clear();
                return true;
            case "ET":
                EndText(frame);
                return true;
            case "Tc":
                _state.CharacterSpacing = operation.Number(0);
                return true;
            case "Tw":
                _state.WordSpacing = operation.Number(0);
                return true;
            case "Tz":
                _state.HorizontalScale = operation.Number(0, 100) / 100;
                return true;
            case "TL":
                _state.Leading = operation.Number(0);
                return true;
            case "Ts":
                _state.Rise = operation.Number(0);
                return true;
            case "Tr":
                _state.TextRenderMode = Math.Clamp((int)operation.Number(0), 0, 7);
                return true;
            case "Tf":
                _state.Font = Name(operation, 0) is { } fontName
                    ? PdfFonts.Create(frame.Resources.Entry("Font", fontName), _document)
                    : null;
                if (_state.Font == null)
                {
                    Tell($"The font '{Name(operation, 0)}' could not be read; its text is not drawn.");
                }

                _state.FontSize = operation.Number(1);
                return true;
            case "Td":
                MoveLine(operation.Number(0), operation.Number(1));
                return true;
            case "TD":
                _state.Leading = -operation.Number(1);
                MoveLine(operation.Number(0), operation.Number(1));
                return true;
            case "Tm":
                _textMatrix = _lineMatrix = Matrix(operation);
                return true;
            case "T*":
                MoveLine(0, -_state.Leading);
                return true;
            case "Tj":
                Show(operation, 0, frame);
                return true;
            case "'":
                MoveLine(0, -_state.Leading);
                Show(operation, 0, frame);
                return true;
            case "\"":
                _state.WordSpacing = operation.Number(0);
                _state.CharacterSpacing = operation.Number(1);
                MoveLine(0, -_state.Leading);
                Show(operation, 2, frame);
                return true;
            case "TJ":
                if (operation.Operands.Count > 0 && operation.Operands[0] is PdfArray items)
                {
                    ShowAll(items, frame);
                }

                return true;
            default:
                return false;
        }
    }

    private void MoveLine(double x, double y)
    {
        _lineMatrix = new PdfMatrix(1, 0, 0, 1, x, y).Then(_lineMatrix);
        _textMatrix = _lineMatrix;
    }

    private void Show(PdfContentOperation operation, int index, PdfFrame frame)
    {
        if (operation.Operands.Count > index && operation.Operands[index] is PdfString text)
        {
            ShowAll(new PdfArray([text]), frame);
        }
    }

    // One string or one TJ array: its glyphs as one run from where the text matrix stands now, the matrix moved past
    // them.
    private void ShowAll(PdfArray items, PdfFrame frame)
    {
        var font = _state.Font;
        if (font == null)
        {
            return;
        }

        var size = _state.FontSize;
        var scale = _state.HorizontalScale;
        var flip = size < 0 ? -1.0 : 1.0;
        var start = _textMatrix;
        var runMatrix = new PdfMatrix(scale * flip, 0, 0, -flip, 0, _state.Rise).Then(start).Then(_state.Ctm);
        var program = font is PdfType3Font ? null : ProgramOf(font);
        var glyphs = new List<PositionedGlyph>();
        var type3 = font as PdfType3Font;
        var type3Glyphs = type3 != null ? new List<(PdfStream Proc, PdfMatrix At)>() : null;
        var vertical = font.IsVertical;
        double x = 0, y = 0;
        foreach (var item in items)
        {
            if (item is PdfInteger or PdfReal)
            {
                var adjust = -(item is PdfInteger integer ? integer.Value : ((PdfReal)item).Value) / 1000 * size;
                if (vertical)
                {
                    y += adjust;
                }
                else
                {
                    x += adjust * scale;
                }

                continue;
            }

            if (item is not PdfString text || text.Bytes == null)
            {
                continue;
            }

            var bytes = text.Bytes;
            for (var position = 0; position < bytes.Length;)
            {
                var character = font.ReadCharacter(bytes, position);
                if (character.Length <= 0)
                {
                    break;
                }

                position += character.Length;
                var advance = font.WidthOf(character);
                if (!double.IsFinite(advance))
                {
                    advance = 0;
                }

                var spacing = _state.CharacterSpacing + (character.IsWordSpace ? _state.WordSpacing : 0);
                if (type3 != null)
                {
                    if (type3.CharProcOf(character) is { } proc)
                    {
                        var at = new PdfMatrix(size * scale, 0, 0, size, x, _state.Rise + y);
                        type3Glyphs.Add((proc, PdfMatrix.From(type3.FontMatrix).Then(at).Then(start)
                            .Then(_state.Ctm)));
                    }
                }
                else if (program != null && font.GlyphOf(character) is var glyph and not 0 && scale != 0)
                {
                    if (vertical && font is PdfType0Font cid)
                    {
                        var (vx, vy) = cid.VerticalOriginOf(character);
                        glyphs.Add(new PositionedGlyph(glyph, -vx * Math.Abs(size), (vy * size - y) * flip));
                    }
                    else
                    {
                        glyphs.Add(new PositionedGlyph(glyph, x / (scale * flip), 0));
                    }
                }

                if (vertical)
                {
                    y += -advance * size + spacing;
                }
                else
                {
                    x += (advance * size + spacing) * scale;
                }
            }
        }

        _textMatrix = new PdfMatrix(1, 0, 0, 1, x, y).Then(start);
        if (type3Glyphs != null)
        {
            DrawType3(type3, type3Glyphs, frame);
        }
        else if (glyphs.Count > 0 && size != 0)
        {
            DrawRun(new GlyphRun(program, Math.Abs(size), glyphs), runMatrix, frame);
        }
    }

    private void DrawRun(GlyphRun run, PdfMatrix runMatrix, PdfFrame frame)
    {
        var mode = _state.TextRenderMode;
        var matrix = PdfMatrices.ToDrawing(runMatrix);
        if (mode >= 4)
        {
            _textClip.Add(new PlacedGlyphRun(run, matrix));
        }

        if (Hidden || mode is 3 or 7)
        {
            return;
        }

        var fills = mode is 0 or 2 or 4 or 6;
        var strokes = mode is 1 or 2 or 5 or 6;
        var fill = fills ? FillBrush() : null;
        Pen stroke = null;
        if (strokes)
        {
            // The line width is in user space; the run's units are the text matrix's and the size's.
            var ratio = PdfMatrices.Scale(runMatrix) / Math.Max(PdfMatrices.Scale(_state.Ctm), 1e-12);
            var width = _state.LineWidth;
            _state.LineWidth = ratio > 0 ? width / ratio : width;
            stroke = StrokePen();
            _state.LineWidth = width;
        }

        var grouped = BeginObjectGroup(frame.Output);
        if (fills && _state.Fill.Pattern != null)
        {
            PaintPattern(_state.Fill, run.Outline, matrix, frame, stroking: false);
            fill = null;
        }

        if (fill != null || stroke != null)
        {
            Write(new PdfGlyphStep(run, fill, stroke, matrix), frame.Output);
        }

        EndObjectGroup(grouped, frame.Output);
    }

    private void EndText(PdfFrame frame)
    {
        if (_textClip.Count > 0)
        {
            OpenGroup(new PdfPushGroupStep { TextClip = [.. _textClip] }, frame.Output);
            _state.OpenGroups = _openGroups;
            _textClip.Clear();
        }
    }

    private IFont ProgramOf(PdfFont font)
    {
        if (!_programs.TryGetValue(font, out var program))
        {
            program = font.Typeface?.Fonts is { Count: > 0 } fonts ? fonts[0] : null;
            if (program == null)
            {
                Tell($"The font '{font.Name}' has no program to draw with; its text is not drawn.");
            }

            _programs[font] = program;
        }

        return program;
    }

    // A Type 3 glyph is a content stream drawn in glyph space. The glyphs of a string are walked one after another, each
    // entering the next as it ends, so a long string takes one frame of depth, not one a glyph.
    private void DrawType3(PdfType3Font font, List<(PdfStream Proc, PdfMatrix At)> glyphs, PdfFrame frame)
    {
        var mode = _state.TextRenderMode;
        if (Hidden || mode is 3 or 7 || glyphs.Count == 0)
        {
            return;
        }

        if (mode >= 4)
        {
            Tell("Text drawn in a Type 3 font does not clip; it is drawn only.");
        }

        EnterGlyph(font.Resources ?? frame.Resources, glyphs, 0, frame.Output);
    }

    private void EnterGlyph(PdfResources resources, List<(PdfStream Proc, PdfMatrix At)> glyphs, int index,
        List<PdfDrawStep> output)
    {
        var (proc, at) = glyphs[index];
        Action<List<PdfDrawStep>> next = index + 1 < glyphs.Count
            ? _ => EnterGlyph(resources, glyphs, index + 1, output)
            : null;
        EnterForm(proc, resources, at, PdfFrameKind.Glyph, output, _state.Copy(), next);
    }
}
