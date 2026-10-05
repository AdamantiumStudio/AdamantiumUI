using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TextBox"/>: an edit field whose value is its text, read by character, word and line.</summary>
public class TextBoxAutomationPeer : UIComponentAutomationPeer, IValueProvider, ITextProvider
{
    public TextBoxAutomationPeer(TextBox owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Edit;

    public string Value => ((TextBox)Owner).Text ?? string.Empty;

    public bool IsReadOnly => ((TextBox)Owner).IsReadOnly;

    public string Text => Value;

    public int SelectionStart => Math.Min(Box().SelectionStart, Box().SelectionStart + Box().SelectionLength);

    public int SelectionLength => Math.Abs(Box().SelectionLength);

    /// <summary>Writes the text as a current value, so a binding on it keeps following.</summary>
    public void SetValue(string value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{AutomationId}' is read-only.");
        }

        Owner.SetCurrentValue(TextBox.TextProperty, value);
    }

    public void Select(int start, int length)
    {
        var end = Math.Clamp(start + length, 0, Text.Length);
        start = Math.Clamp(start, 0, Text.Length);
        Owner.SetCurrentValue(TextBoxBase.SelectionStartProperty, start);
        Owner.SetCurrentValue(TextBoxBase.SelectionLengthProperty, end - start);
        Owner.SetCurrentValue(TextBoxBase.CaretIndexProperty, end);
    }

    public int LineOf(int index) => Box().LineOfIndex(index);

    public (int Start, int End) LineRange(int line) => Box().LineIndexRange(line);

    public IReadOnlyList<Rect> Bounds(int start, int end) => Box().TextSurface is { } surface
        ? Box().SurfaceRects(start, end).Select(rect => ScreenRect(surface, rect)).ToList()
        : [];

    public int IndexAt(PixelPoint screen)
    {
        if (Box().TextSurface is not { } surface)
        {
            return 0;
        }

        var local = LocalPoint(surface, screen);
        return Box().IndexAtSurfacePoint(local.X, local.Y);
    }

    public void ScrollIntoView(int index) => Box().ScrollIndexIntoView(index);

    /// <summary>Its placeholder, when nothing else names it: the words that say what goes in.</summary>
    protected override string NameCore() => ((TextBox)Owner).Placeholder is { Length: > 0 } placeholder ? placeholder : null;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];

    private TextBox Box() => (TextBox)Owner;
}
