using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Pdf;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.UI.Core;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>Reads a page's content into draws (ISO 32000-2, 8 and 9): the page's streams, then its annotations'
/// appearances, each form, Type 3 glyph, soft mask and pattern cell walked on one explicit stack. A form already being
/// drawn is not entered again, nor one nested past <see cref="MaxDepth"/>; no more than <see cref="MaxFormDraws"/>
/// streams are entered in all, nor <see cref="MaxSteps"/> draws written - each limit reached is told in
/// <see cref="Problems"/>. A stream that cannot be read ends where it breaks.</summary>
internal sealed partial class PdfPageInterpreter
{
    public const int MaxDepth = 32;
    public const int MaxFormDraws = 10_000;
    public const int MaxGlyphDraws = 1_000_000;
    public const int MaxSteps = 4_000_000;
    public const int MaxProblems = 100;

    private readonly PdfDocument _document;
    private readonly PdfPageDrawingOptions _options;
    private readonly CancellationToken _cancellation;
    private readonly PdfOptionalContent _optionalContent;
    private readonly Stack<PdfFrame> _frames = new();
    private readonly Stack<PdfGraphicsState> _states = new();
    private readonly HashSet<PdfStream> _onPath = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfStream, ReadOnlyMemory<byte>?> _streams = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _problems = [];
    private readonly HashSet<string> _toldOperators = [];
    private PdfGraphicsState _state;
    private int _openGroups;
    private int _streamsEntered;
    private int _glyphsEntered;
    private int _steps;

    // Optional content hidden by BDC /OC: how deep in marked content the walk is, and from which depth on it draws
    // nothing; -1 while it draws.
    private int _markedDepth;
    private int _hiddenFrom = -1;

    public PdfPageInterpreter(PdfDocument document, PdfPageDrawingOptions options, CancellationToken cancellation)
    {
        _document = document;
        _options = options;
        _cancellation = cancellation;
        _optionalContent = PdfOptionalContent.Of(document,
            options.Print ? PdfOptionalContentUse.Print : PdfOptionalContentUse.View);
    }

    public IReadOnlyList<string> Problems => _problems;

    public List<PdfDrawStep> Draw(PdfPage page)
    {
        var output = new List<PdfDrawStep>();
        var pageMatrix = PageMatrix(page);
        _pageBox = new Rect(0, 0, page.Width, page.Height);
        _state = InitialState(pageMatrix);
        var resources = new PdfResources(page.Resources, _document);
        ReadOnlyMemory<byte> contents;
        try
        {
            contents = page.GetContents();
        }
        catch (PdfException exception)
        {
            Tell("The page's contents could not be read: " + exception.Message);
            contents = ReadOnlyMemory<byte>.Empty;
        }

        Enter(new PdfFrame(PdfFrameKind.Page, PdfContentReader.Read(contents).GetEnumerator(), resources, output,
            pageMatrix, _states.Count, _openGroups, null));
        Walk();
        if (_options.Annotations)
        {
            DrawAnnotations(page, pageMatrix, output);
        }

        CloseGroupsDownTo(0, output);
        return output;
    }

    // Default user space - y up, from the crop box's corner - to the page's units: y down from its top left, turned by
    // /Rotate, scaled by /UserUnit.
    private static PdfMatrix PageMatrix(PdfPage page)
    {
        var box = page.CropBox;
        var (w, h) = (box.Width, box.Height);
        var upright = new PdfMatrix(1, 0, 0, -1, -Math.Min(box.Left, box.Right), Math.Max(box.Top, box.Bottom));
        var turned = (((page.Rotation % 360) + 360) % 360) switch
        {
            90 => new PdfMatrix(0, 1, -1, 0, h, 0),
            180 => new PdfMatrix(-1, 0, 0, -1, w, h),
            270 => new PdfMatrix(0, -1, 1, 0, 0, w),
            _ => PdfMatrix.Identity,
        };
        var unit = page.UserUnit > 0 ? page.UserUnit : 1;
        return upright.Then(turned).Then(new PdfMatrix(unit, 0, 0, unit, 0, 0));
    }

    private static PdfGraphicsState InitialState(PdfMatrix ctm)
    {
        var gray = new PdfDeviceGrayColorSpace();
        return new PdfGraphicsState
        {
            Ctm = ctm,
            Fill = PdfPaint.Initial(gray),
            Stroke = PdfPaint.Initial(gray),
        };
    }

    private void Enter(PdfFrame frame)
    {
        _frames.Push(frame);
        if (frame.Form != null && frame.Kind != PdfFrameKind.Glyph)
        {
            _onPath.Add(frame.Form);
        }
    }

    private void Walk()
    {
        while (_frames.Count > 0)
        {
            _cancellation.ThrowIfCancellationRequested();
            var frame = _frames.Peek();
            if (_steps >= MaxSteps || !Advance(frame))
            {
                Leave(frame);
                continue;
            }

            try
            {
                Execute(frame.Operations.Current, frame);
            }
            catch (PdfException exception)
            {
                Tell($"'{frame.Operations.Current.Operator}' could not be drawn: {exception.Message}");
            }
        }
    }

    private bool Advance(PdfFrame frame)
    {
        try
        {
            return frame.Operations.MoveNext();
        }
        catch (PdfException exception)
        {
            Tell("A content stream breaks off: " + exception.Message);
            return false;
        }
    }

    private void Leave(PdfFrame frame)
    {
        _frames.Pop();
        frame.Operations.Dispose();
        if (frame.Form != null && frame.Kind != PdfFrameKind.Glyph)
        {
            _onPath.Remove(frame.Form);
        }

        // The states it saved and left unrestored, then the one saved for it on entry.
        while (_states.Count > frame.Floor)
        {
            _state = _states.Pop();
        }

        CloseGroupsDownTo(frame.GroupsAtStart + frame.OwnGroups, frame.Output);
        CloseGroupsDownTo(frame.GroupsAtStart, frame.Output);
        if (frame.Kind != PdfFrameKind.Page && _states.Count > 0 && _states.Count == frame.Floor)
        {
            _state = _states.Pop();
        }

        frame.Finish?.Invoke(frame.Output);
    }

    private void Execute(PdfContentOperation operation, PdfFrame frame)
    {
        // Inside hidden optional content the state still changes - a clip, a color, the text position - but nothing
        // paints: each painting operator asks Hidden.
        switch (operation.Operator)
        {
            case "q":
                _states.Push(_state.Copy());
                break;
            case "Q":
                Restore(frame);
                break;
            case "cm":
                _state.Ctm = Matrix(operation).Then(_state.Ctm);
                break;
            case "w":
                _state.LineWidth = Math.Max(operation.Number(0, 1), 0);
                break;
            case "J":
                _state.LineCap = Math.Clamp((int)operation.Number(0), 0, 2);
                break;
            case "j":
                _state.LineJoin = Math.Clamp((int)operation.Number(0), 0, 2);
                break;
            case "M":
                _state.MiterLimit = Math.Max(operation.Number(0, 10), 1);
                break;
            case "d":
                _state.Dash = operation.Operands.Count >= 2
                    ? PdfDashPattern.From(operation.Operands[0], operation.Number(1), _document)
                    : PdfDashPattern.Solid;
                break;
            case "gs":
                SetParameters(operation, frame);
                break;
            case "ri":
            case "i":
            case "d0":
            case "d1":
                break;
            case "BMC":
                _markedDepth++;
                break;
            case "BDC":
                _markedDepth++;
                if (_hiddenFrom < 0 && !VisibleMarkedContent(operation, frame.Resources))
                {
                    _hiddenFrom = _markedDepth;
                }

                break;
            case "EMC":
                if (_markedDepth == _hiddenFrom)
                {
                    _hiddenFrom = -1;
                }

                _markedDepth = Math.Max(_markedDepth - 1, 0);
                break;
            case "BX":
            case "EX":
            case "MP":
            case "DP":
                break;
            default:
                if (!PathOperator(operation, frame) && !ColorOperator(operation, frame) &&
                    !TextOperator(operation, frame) && !ObjectOperator(operation, frame))
                {
                    if (_toldOperators.Add(operation.Operator))
                    {
                        Tell($"The operator '{operation.Operator}' is not known.");
                    }
                }

                break;
        }
    }

    /// <summary>Whether optional content that is off hides what is drawn now.</summary>
    private bool Hidden => _hiddenFrom >= 0;

    private void Restore(PdfFrame frame)
    {
        if (_states.Count <= frame.Floor)
        {
            return;
        }

        var restored = _states.Pop();
        CloseGroupsDownTo(Math.Max(restored.OpenGroups, frame.GroupsAtStart + frame.OwnGroups), frame.Output);
        _state = restored;
    }

    private bool VisibleMarkedContent(PdfContentOperation operation, PdfResources resources)
    {
        if (operation.Operands.Count < 2 || operation.Operands[0] is not PdfName { Value: "OC" })
        {
            return true;
        }

        var membership = operation.Operands[1] is PdfName name ? resources.Properties(name.Value) : operation.Operands[1];
        return _optionalContent.IsVisible(membership);
    }

    private void SetParameters(PdfContentOperation operation, PdfFrame frame)
    {
        if (operation.Operands.Count == 0 || operation.Operands[0] is not PdfName name ||
            frame.Resources.ExtGState(name.Value) is not { } dictionary)
        {
            return;
        }

        var parameters = PdfGraphicsStateParameters.From(dictionary, frame.Resources, _document);
        Apply(parameters, frame);
    }

    private void Apply(PdfGraphicsStateParameters parameters, PdfFrame frame)
    {
        if (parameters.LineWidth is { } width) _state.LineWidth = width;
        if (parameters.LineCap is { } cap) _state.LineCap = cap;
        if (parameters.LineJoin is { } join) _state.LineJoin = join;
        if (parameters.MiterLimit is { } miter) _state.MiterLimit = miter;
        if (parameters.Dash is { } dash) _state.Dash = dash;
        if (parameters.StrokeAlpha is { } strokeAlpha) _state.StrokeAlpha = strokeAlpha;
        if (parameters.FillAlpha is { } fillAlpha) _state.FillAlpha = fillAlpha;
        if (parameters.BlendMode is { } blend) _state.BlendMode = blend;
        if (parameters.Font != null)
        {
            _state.Font = parameters.Font;
            _state.FontSize = parameters.FontSize ?? _state.FontSize;
        }

        if (parameters.SetsSoftMask)
        {
            _state.SoftMask = null;
            if (parameters.SoftMask != null)
            {
                BeginSoftMask(parameters.SoftMask, frame);
            }
        }
    }

    // Opens groups so that what follows is cut, masked or blended; every group is closed when the state that opened it
    // is restored, or when the stream ends.
    private void OpenGroup(PdfPushGroupStep group, List<PdfDrawStep> output)
    {
        Write(group, output);
        _openGroups++;
    }

    private void CloseGroupsDownTo(int count, List<PdfDrawStep> output)
    {
        while (_openGroups > count)
        {
            output.Add(PdfPopGroupStep.Instance);
            _openGroups--;
        }
    }

    private void Write(PdfDrawStep step, List<PdfDrawStep> output)
    {
        if (_steps >= MaxSteps)
        {
            return;
        }

        if (++_steps == MaxSteps)
        {
            Tell($"The page has more than {MaxSteps} draws; the rest are not drawn.");
        }

        output.Add(step);
    }

    private ReadOnlyMemory<byte>? StreamData(PdfStream stream)
    {
        if (!_streams.TryGetValue(stream, out var data))
        {
            try
            {
                data = _document.Decode(stream).Data;
            }
            catch (PdfException exception)
            {
                Tell("A stream could not be decoded: " + exception.Message);
                data = null;
            }

            _streams[stream] = data;
        }

        return data;
    }

    // A Type 3 glyph may be drawn many times over in one string, its frames on the stack together, so it is not kept
    // off its own path - depth and its own count bound a glyph that shows itself.
    private bool CanEnter(PdfStream stream, PdfFrameKind kind)
    {
        if (kind != PdfFrameKind.Glyph && _onPath.Contains(stream))
        {
            Tell("A form draws itself; it is not drawn again inside itself.");
            return false;
        }

        if (_frames.Count > MaxDepth)
        {
            Tell($"Forms nest deeper than {MaxDepth}; the deeper ones are not drawn.");
            return false;
        }

        if (kind == PdfFrameKind.Glyph)
        {
            if (++_glyphsEntered == MaxGlyphDraws + 1)
            {
                Tell($"The page draws more than {MaxGlyphDraws} Type 3 glyphs; the rest are not drawn.");
            }

            return _glyphsEntered <= MaxGlyphDraws;
        }

        if (++_streamsEntered == MaxFormDraws + 1)
        {
            Tell($"The page draws more than {MaxFormDraws} forms; the rest are not drawn.");
        }

        return _streamsEntered <= MaxFormDraws;
    }

    /// <summary>The numbers of an array, or null when it is not one; an entry that is not a number reads as 0.</summary>
    private double[] Numbers(PdfObject value)
    {
        if (_document.Resolve(value) is not PdfArray array)
        {
            return null;
        }

        var numbers = new double[array.Count];
        for (var i = 0; i < numbers.Length; i++)
        {
            numbers[i] = _document.Resolve(array[i]) switch
            {
                PdfInteger integer => integer.Value,
                PdfReal real when double.IsFinite(real.Value) => real.Value,
                _ => 0,
            };
        }

        return numbers;
    }

    private void Tell(string problem)
    {
        if (_problems.Count < MaxProblems)
        {
            _problems.Add(problem);
        }
    }

    private static PdfMatrix Matrix(PdfContentOperation operation) =>
        operation.Operands.Count >= 6
            ? PdfMatrix.From([
                operation.Number(0), operation.Number(1), operation.Number(2), operation.Number(3),
                operation.Number(4), operation.Number(5),
            ])
            : PdfMatrix.Identity;

    private static string Name(PdfContentOperation operation, int index) =>
        operation.Operands.Count > index && operation.Operands[index] is PdfName name ? name.Value : null;
}
