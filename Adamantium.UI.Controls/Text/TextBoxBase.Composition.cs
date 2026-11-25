using System;
using Adamantium.Core.Shared.Fonts;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

public abstract partial class TextBoxBase
{
    private bool _composing;
    private string _composition;
    private int _compositionCursor;
    private IFont _compositionFont;
    private TextLayout _compositionLayout;
    private (string Text, double FontSize) _compositionShaped;

    private void OnCompositionStarted(object sender, TextCompositionEventArgs e)
    {
        _composing = true;
        InvalidateSurface();
    }

    private void OnCompositionChanged(object sender, TextCompositionEventArgs e)
    {
        if (e.IsShownBySystem || IsReadOnly)
        {
            return;
        }

        _composition = string.IsNullOrEmpty(e.Text) ? null : e.Text;
        _compositionCursor = Math.Clamp(e.CursorPosition, 0, e.Text?.Length ?? 0);
        InvalidateSurface(true);
        ScrollCompositionIntoView();
    }

    private void EndComposition()
    {
        _composing = false;   // a composition the focus left unended composes nothing here any more
        InvalidateSurface(_composition != null);
        _composition = null;
    }

    // How far along the caret's line what is composed reaches.
    private double CompositionEnd()
    {
        if (_composition == null)
        {
            return 0;
        }

        ShapeComposition();
        return CaretRect(CaretIndex).X + CompositionLength;
    }

    private double CompositionLength => Vertical
        ? _compositionLayout.CalculatedLayoutSize.Height
        : _compositionLayout.CalculatedLayoutSize.Width;

    private void ScrollCompositionIntoView()
    {
        if (_composition == null || _scrollViewer == null)
        {
            return;
        }

        ShapeComposition();
        var caret = SurfaceCaret(CaretIndex);
        _scrollViewer.BringIntoView(Vertical
            ? new Rect(caret.X, caret.Y, caret.Width, CompositionLength + CaretPadding)
            : new Rect(caret.X, caret.Y, CompositionLength + CaretPadding, caret.Height));
    }

    // Over the text after the caret, underlined, as it is not yet part of it; the caret stands in it.
    private Rect DrawComposition(IDrawingSession session, Size<double> size, Rect caret)
    {
        ShapeComposition();
        var length = _compositionLayout.CalculatedLayoutSize;
        var x = Vertical ? caret.X + caret.Width - length.Width : Math.Max(0, Math.Min(caret.X, size.Width - length.Width));
        var y = Vertical ? Math.Max(0, Math.Min(caret.Y, size.Height - length.Height)) : caret.Y;
        (x, y) = (Math.Floor(x), Math.Floor(y));
        session.DrawRectangle(Background ?? Brushes.Transparent, new Rect(x, y, length.Width, length.Height));
        session.DrawText(BuildTextParameters(Foreground, x, y, size), size, _compositionLayout, Foreground,
            Brushes.Transparent, Brushes.Transparent);
        session.DrawRectangle(Foreground, Vertical
            ? new Rect(x, y, 1, length.Height)
            : new Rect(x, y + length.Height - 1, length.Width, 1));

        var along = _compositionLayout.GetCaretPoint(new CaretPosition(_compositionCursor, false)).X;
        return Vertical
            ? new Rect(caret.X, y + along, caret.Width, CaretWidth)
            : new Rect(x + along, caret.Y, CaretWidth, caret.Height);
    }

    private void ShapeComposition()
    {
        var loadsSeen = TypefaceStore.LoadedCount;
        var fontReady = TryResolveFont(FontFamily ?? DefaultFontFamily, out var font);
        if (_compositionLayout == null || !ReferenceEquals(_compositionFont, font))
        {
            _compositionLayout = new TextLayout(font.Typeface, font);
            _compositionFont = font;
            _compositionShaped = default;
        }

        if (_compositionShaped == (_composition, FontSize))
        {
            return;
        }

        _compositionLayout.LoadFontsInBackground = !FontAtlasStore.SynchronousFill;
        _compositionLayout.WritingMode = WritingMode;
        _compositionLayout.Direction = TextDirection;
        _compositionLayout.ProcessText(_composition, FontSize, new Size<double>(double.NaN, double.NaN),
            TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        _compositionShaped = (_composition, FontSize);
        if (!fontReady || _compositionLayout.HasPendingFonts)
        {
            _compositionShaped = default;
            WaitForFonts(loadsSeen);
        }
    }
}
