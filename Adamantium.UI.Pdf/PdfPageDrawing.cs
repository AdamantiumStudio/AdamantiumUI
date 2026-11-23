using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Pdf.Interpretation;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf;

/// <summary>A PDF page as a drawing: read once, then drawn at any size and zoom without being read again. Its units are
/// the page's points (times its <c>/UserUnit</c>), y down, from the top left corner of the crop box, the page's
/// <c>/Rotate</c> already applied. What could not be drawn is told in <see cref="Problems"/>; the rest is drawn.
/// <para>Built on any thread, but the pages of one document one at a time - a document is read from one thread at a
/// time.</para></summary>
public sealed class PdfPageDrawing : Drawing
{
    private readonly IReadOnlyList<PdfDrawStep> _steps;
    private readonly IReadOnlyList<PdfPressableSteps> _pressable;
    private readonly PdfPressableSteps _pressed;
    private readonly Prepared _prepared;
    private readonly int _level;
    private readonly Rect _view;

    private PdfPageDrawing(IReadOnlyList<PdfDrawStep> steps, Rect bounds, IReadOnlyList<string> problems,
        IReadOnlyList<PdfPressableSteps> pressable, PdfPressableSteps pressed, Prepared prepared, int level, Rect view)
    {
        _steps = steps;
        Bounds = bounds;
        Problems = problems;
        _pressable = pressable;
        _pressed = pressed;
        _prepared = prepared;
        _level = level;
        _view = view;
    }

    // What PrepareForDrawing works out, shared by every view of the page: the long paths' levels (PdfPathDetail) and
    // where each draw stands, for drawing only what meets a view.
    private sealed class Prepared
    {
        public Dictionary<PdfDrawStep, PdfPathDetail> ByStep;

        public PdfStepPlaces Places;
    }

    public override Rect Bounds { get; }

    /// <summary>What was not drawn, and why - a stream that could not be read, an operator not known, a limit reached.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    internal IReadOnlyList<PdfDrawStep> Steps => _steps;

    /// <summary>The paths the page's content draws, in the order drawn - what a measurement snaps to; not the covers a
    /// pattern or a mask lays. Made anew each call, so called once a sheet.</summary>
    public IReadOnlyList<PdfDrawnPath> CollectPaths()
    {
        var paths = new List<PdfDrawnPath>();
        foreach (var step in _steps)
        {
            if (step is PdfGeometryStep { IsPagePath: true, Geometry: not null } drawn)
            {
                paths.Add(new PdfDrawnPath(drawn.Geometry, drawn.Matrix, drawn.Stroke != null, drawn.Fill != null));
            }
        }

        return paths;
    }

    public static PdfPageDrawing Build(PdfDocument document, PdfPage page, PdfPageDrawingOptions options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);
        var interpreter = new PdfPageInterpreter(document, options ?? PdfPageDrawingOptions.Default, cancellation);
        var bounds = new Rect(0, 0, page.Width, page.Height);
        var steps = PdfPathJoin.Join(interpreter.Draw(page), interpreter.Pressable, out var pressable);
        return new PdfPageDrawing(steps, bounds, interpreter.Problems, pressable, null, new Prepared(), 0, Rect.Empty);
    }

    /// <summary>Makes on the calling thread, on all the processor's cores, what drawing the page's paths takes - their
    /// outlines and their fills' tiles - which its first draw would make on the loop thread, the window standing still
    /// for as long, and how much each point of its long paths matters, for <see cref="AtDetail"/>. Stops when
    /// <paramref name="cancellation"/> is cancelled; what is not made then, or fails to be made, is made when drawn, or
    /// drawn as given. Only running out of memory is let through.</summary>
    public void PrepareForDrawing(CancellationToken cancellation = default)
    {
        PrepareGeometries(cancellation);
        WorkOutPathLevels(cancellation);
        if (!cancellation.IsCancellationRequested)
        {
            // After the paths are prepared: that works out their bounds.
            _prepared.Places = PdfStepPlaces.Of(_steps);
        }
    }

    private void PrepareGeometries(CancellationToken cancellation)
    {
        // A path drawn twice is prepared once - by one thread - with a fill if either draw has one.
        var paths = new Dictionary<Geometry, Brush>(ReferenceEqualityComparer.Instance);
        var lists = new Stack<IReadOnlyList<PdfDrawStep>>();
        lists.Push(_steps);
        foreach (var pressable in _pressable)
        {
            lists.Push(pressable.Down);
        }

        // A soft mask several groups share is gone through once.
        var seen = new HashSet<IReadOnlyList<PdfDrawStep>>(ReferenceEqualityComparer.Instance);
        while (lists.Count > 0)
        {
            var steps = lists.Pop();
            if (seen.Add(steps))
            {
                CollectGeometries(steps, paths, lists);
            }
        }

        var work = new List<KeyValuePair<Geometry, Brush>>(paths);
        OnEveryCore(work.Count, (next, buffers) => work[next].Key.PrepareForDrawing(work[next].Value, buffers),
            cancellation);
    }

    private void WorkOutPathLevels(CancellationToken cancellation)
    {
        var paths = new List<PdfGeometryStep>();
        foreach (var step in _steps)
        {
            if (step is PdfGeometryStep { Geometry: not null } path && (path.Fill != null || path.Stroke != null))
            {
                paths.Add(path);
            }
        }

        var details = new PdfPathDetail[paths.Count];
        OnEveryCore(paths.Count, (next, _) => details[next] = PdfPathDetail.Of(paths[next]), cancellation);
        var byStep = new Dictionary<PdfDrawStep, PdfPathDetail>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < paths.Count; i++)
        {
            if (details[i] != null)
            {
                byStep[paths[i]] = details[i];
            }
        }

        _prepared.ByStep = byStep;
    }

    // One worker to a core takes the next item while there is one, on buffers of its own from item to item - let go
    // with the work. An item that fails is left to its draw, which fails, or not, as it did before it was prepared.
    private static void OnEveryCore(int count, Action<int, CurveTileBuffers> work, CancellationToken cancellation)
    {
        var taken = -1;
        try
        {
            Parallel.For(0, Math.Min(Environment.ProcessorCount, count),
                new ParallelOptions { CancellationToken = cancellation }, _ =>
                {
                    var buffers = new CurveTileBuffers();
                    for (var next = Interlocked.Increment(ref taken);
                         next < count && !cancellation.IsCancellationRequested;
                         next = Interlocked.Increment(ref taken))
                    {
                        try
                        {
                            work(next, buffers);
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                        }
                    }
                });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (AggregateException failed)
        {
            // Out of memory - all a worker lets out - as one thread would throw it.
            ExceptionDispatchInfo.Capture(failed.InnerExceptions[0]).Throw();
        }
    }

    // A group's clip is filled to cut it, and its soft mask's steps are drawn too.
    private static void CollectGeometries(IReadOnlyList<PdfDrawStep> steps, Dictionary<Geometry, Brush> into,
        Stack<IReadOnlyList<PdfDrawStep>> masks)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case PdfGeometryStep { Geometry: { } geometry } drawn:
                    if (!into.TryGetValue(geometry, out var fill) || fill == null)
                    {
                        into[geometry] = drawn.Fill;
                    }

                    break;
                case PdfPushGroupStep group:
                    if (group.Clip != null)
                    {
                        into[group.Clip] = Brushes.Black;
                    }

                    if (group.Mask is PdfContentDrawing mask)
                    {
                        masks.Push(mask.Steps);
                    }

                    break;
            }
        }
    }

    /// <summary>Whether <paramref name="annotation"/> was drawn with a down appearance to show while it is pressed.
    /// </summary>
    public bool CanPress(PdfAnnotation annotation) => PressableOf(annotation) != null;

    /// <summary>The page with <paramref name="annotation"/> drawn by its down appearance in place of its normal one;
    /// with none pressed for one with no down appearance, or null.</summary>
    public PdfPageDrawing Pressing(PdfAnnotation annotation)
    {
        var pressable = annotation == null ? null : PressableOf(annotation);
        return ReferenceEquals(pressable, _pressed)
            ? this
            : new PdfPageDrawing(_steps, Bounds, Problems, _pressable, pressable, _prepared, _level, _view);
    }

    /// <summary>Whether <see cref="AtDetail"/> at <paramref name="finestPixelsPerPoint"/> draws only copies of its long
    /// paths already made - by <see cref="PrepareDetail"/> or an earlier draw - or the paths as given: its first draw
    /// makes none on the loop thread.</summary>
    public bool IsPreparedAt(double finestPixelsPerPoint)
    {
        var level = PdfPathDetail.LevelFor(finestPixelsPerPoint);
        if (level == 0 || _prepared.ByStep is not { } details)
        {
            return true;
        }

        foreach (var detail in details.Values)
        {
            if (!detail.IsMadeAt(level))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Makes on the calling thread, on all the processor's cores, the copies of its long paths that
    /// <see cref="AtDetail"/> at <paramref name="finestPixelsPerPoint"/> draws, with what drawing them takes - their
    /// outlines and their fills' tiles - which their first draw would make on the loop thread, the window standing still
    /// for as long. Nothing before <see cref="PrepareForDrawing"/>; stops when <paramref name="cancellation"/> is
    /// cancelled, and what is not made then is made when drawn. Only running out of memory is let through.</summary>
    public void PrepareDetail(double finestPixelsPerPoint, CancellationToken cancellation = default)
    {
        var level = PdfPathDetail.LevelFor(finestPixelsPerPoint);
        if (level == 0 || _prepared.ByStep is not { } details)
        {
            return;
        }

        var work = new List<PdfPathDetail>(details.Count);
        foreach (var detail in details.Values)
        {
            if (!detail.IsMadeAt(level))
            {
                work.Add(detail);
            }
        }

        OnEveryCore(work.Count, (next, buffers) => work[next].PrepareAt(level, buffers), cancellation);
    }

    /// <summary>The page with its long paths - lines and areas - drawn by as few of their points as a page shown at no
    /// more than <paramref name="finestPixelsPerPoint"/> device pixels to its point takes - those left out within a
    /// quarter of a pixel of the path - once <see cref="PrepareForDrawing"/> has worked out how much each point matters;
    /// as given before. The copies <see cref="PrepareDetail"/> has not made are made at the first draw.</summary>
    public PdfPageDrawing AtDetail(double finestPixelsPerPoint)
    {
        var level = PdfPathDetail.LevelFor(finestPixelsPerPoint);
        return level == _level
            ? this
            : new PdfPageDrawing(_steps, Bounds, Problems, _pressable, _pressed, _prepared, level, _view);
    }

    /// <summary>The page drawn only where it meets <paramref name="view"/>, in the page's units - what is wholly outside
    /// left out - once <see cref="PrepareForDrawing"/> has worked out where each draw stands; all of it before, and for
    /// an empty view.</summary>
    public PdfPageDrawing Seen(Rect view) => view == _view
        ? this
        : new PdfPageDrawing(_steps, Bounds, Problems, _pressable, _pressed, _prepared, _level, view);

    public override void Render(IDrawingSession session, Matrix4x4F transform, Brush currentColor)
    {
        var places = _view.IsEmpty ? null : _prepared.Places;
        if (_pressed == null)
        {
            Draw(session, transform, _steps, 0, _steps.Count, places);
            return;
        }

        Draw(session, transform, _steps, 0, _pressed.Start, places);
        Draw(session, transform, _pressed.Down, 0, _pressed.Down.Count, null);
        Draw(session, transform, _steps, _pressed.End, _steps.Count, places);
    }

    private PdfPressableSteps PressableOf(PdfAnnotation annotation)
    {
        foreach (var pressable in _pressable)
        {
            if (ReferenceEquals(pressable.Annotation, annotation.Dictionary))
            {
                return pressable;
            }
        }

        return null;
    }

    // A draw that does not meet the view is left out; a group that does not, with all it holds - when it ends within
    // these draws; one running past them is begun, what it holds left out draw by draw. An end of a group is never left
    // out alone: its beginning was drawn.
    private void Draw(IDrawingSession session, Matrix4x4F transform, IReadOnlyList<PdfDrawStep> steps, int from, int to,
        PdfStepPlaces places)
    {
        var lines = _level > 0 ? _prepared.ByStep : null;
        for (var index = from; index < to; index++)
        {
            if (places != null && steps[index] is not PdfPopGroupStep && !places.Meets(index, _view))
            {
                var end = places.EndOf(index);
                if (end < 0)
                {
                    continue;
                }

                if (end < to)
                {
                    index = end;
                    continue;
                }
            }

            var step = steps[index];
            if (lines != null && lines.TryGetValue(step, out var detail))
            {
                step = detail.StepAt(_level);
            }

            step.Draw(session, transform);
        }
    }
}
