using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComClass]
internal sealed partial class UiaTextRange : IUiaTextRangeProvider
{
    private const int InvalidOperation = unchecked((int)0x80131509);
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int IsReadOnlyAttribute = 40015;

    private readonly UiaProvider _owner;
    private int _start;
    private int _end;

    public UiaTextRange(UiaProvider owner, int start, int end)
    {
        _owner = owner;
        _start = start;
        _end = end;
    }

    public IUiaTextRangeProvider Clone() => new UiaTextRange(_owner, _start, _end);

    public bool Compare(IUiaTextRangeProvider range) =>
        range is UiaTextRange other && ReferenceEquals(other._owner, _owner) && other._start == _start && other._end == _end;

    public int CompareEndpoints(UiaTextEndpoint endpoint, IUiaTextRangeProvider targetRange, UiaTextEndpoint targetEndpoint) =>
        Endpoint(endpoint) - Same(targetRange).Endpoint(targetEndpoint);

    public void ExpandToEnclosingUnit(UiaTextUnit unit) => Bridge().Run(() =>
    {
        var text = Provider().Text;
        var start = Math.Clamp(_start, 0, text.Length);
        if (start == text.Length && text.Length > 0)
        {
            start = Previous(unit, start);
        }
        else if (!IsBoundary(unit, start))
        {
            start = Previous(unit, start);
        }

        _start = start;
        _end = Next(unit, start);
    });

    public IUiaTextRangeProvider FindAttribute(int attributeId, UiaVariant value, bool backward) => null;

    public IUiaTextRangeProvider FindText(string text, bool backward, bool ignoreCase) => Bridge().Run(() =>
    {
        var range = Provider().Text[_start.._end];
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var at = backward ? range.LastIndexOf(text, comparison) : range.IndexOf(text, comparison);
        return at < 0 || text.Length == 0 ? null : (IUiaTextRangeProvider)new UiaTextRange(_owner, _start + at, _start + at + text.Length);
    });

    public UiaVariant GetAttributeValue(int attributeId) => Bridge().Run(() => attributeId == IsReadOnlyAttribute
        ? UiaVariant.From(_owner.FindPeer()?.GetPattern(PatternId.Value) is IValueProvider { IsReadOnly: true })
        : UiaVariant.NotSupported());

    public nint GetBoundingRectangles() => Bridge().Run(() => _start == _end
        ? UiaSafeArray.Of(Array.Empty<double>())
        : UiaSafeArray.Of(Provider().Bounds(_start, _end).SelectMany(r => new[] { r.X, r.Y, r.Width, r.Height }).ToArray()));

    public IRawElementProviderSimple GetEnclosingElement() => _owner;

    public string GetText(int maxLength) => Bridge().Run(() =>
    {
        var text = Provider().Text;
        var start = Math.Clamp(_start, 0, text.Length);
        var length = Math.Clamp(_end, start, text.Length) - start;
        return text.Substring(start, maxLength >= 0 ? Math.Min(maxLength, length) : length);
    });

    public int Move(UiaTextUnit unit, int count) => Bridge().Run(() =>
    {
        if (count == 0)
        {
            return 0;
        }

        var length = Provider().Text.Length;
        var degenerate = _start == _end;
        var position = degenerate || IsBoundary(unit, _start) ? _start : Previous(unit, _start);
        var moved = 0;
        while (moved != count)
        {
            var next = count > 0 ? Next(unit, position) : Previous(unit, position);
            if (next == position || (!degenerate && next == length && count > 0))
            {
                break;
            }

            position = next;
            moved += Math.Sign(count);
        }

        _start = position;
        _end = degenerate ? position : Next(unit, position);
        return moved;
    });

    public int MoveEndpointByUnit(UiaTextEndpoint endpoint, UiaTextUnit unit, int count) => Bridge().Run(() =>
    {
        var position = Endpoint(endpoint);
        var moved = 0;
        while (moved != count)
        {
            var next = count > 0 ? Next(unit, position) : Previous(unit, position);
            if (next == position)
            {
                break;
            }

            position = next;
            moved += Math.Sign(count);
        }

        SetEndpoint(endpoint, position);
        return moved;
    });

    public void MoveEndpointByRange(UiaTextEndpoint endpoint, IUiaTextRangeProvider targetRange, UiaTextEndpoint targetEndpoint) =>
        SetEndpoint(endpoint, Same(targetRange).Endpoint(targetEndpoint));

    public void Select() => Bridge().Run(() => Provider().Select(_start, _end - _start));

    public void AddToSelection() => throw new COMException("The text holds one selection.", InvalidOperation);

    public void RemoveFromSelection() => throw new COMException("The text holds one selection.", InvalidOperation);

    public void ScrollIntoView(bool alignToTop) => Bridge().Run(() => Provider().ScrollIntoView(alignToTop ? _start : _end));

    public nint GetChildren() => UiaSafeArray.NoObjects();

    private UiaBridge Bridge() => _owner.Bridge;

    private ITextProvider Provider() => _owner.FindPeer()?.GetPattern(PatternId.Text) as ITextProvider
        ?? throw new COMException("The element no longer holds text.", UiaBridge.ElementNotAvailable);

    private UiaTextRange Same(IUiaTextRangeProvider range) =>
        range is UiaTextRange other && ReferenceEquals(other._owner, _owner)
            ? other
            : throw new COMException("The range is of another element.", InvalidArgument);

    private int Endpoint(UiaTextEndpoint endpoint) => endpoint == UiaTextEndpoint.Start ? _start : _end;

    private void SetEndpoint(UiaTextEndpoint endpoint, int position)
    {
        if (endpoint == UiaTextEndpoint.Start)
        {
            _start = position;
            _end = Math.Max(_end, position);
        }
        else
        {
            _end = position;
            _start = Math.Min(_start, position);
        }
    }

    private int Next(UiaTextUnit unit, int position)
    {
        var length = Provider().Text.Length;
        for (var i = position + 1; i < length; i++)
        {
            if (IsBoundary(unit, i))
            {
                return i;
            }
        }

        return length;
    }

    private int Previous(UiaTextUnit unit, int position)
    {
        for (var i = position - 1; i > 0; i--)
        {
            if (IsBoundary(unit, i))
            {
                return i;
            }
        }

        return 0;
    }

    private bool IsBoundary(UiaTextUnit unit, int position)
    {
        var provider = Provider();
        var text = provider.Text;
        if (position <= 0 || position >= text.Length)
        {
            return true;
        }

        return unit switch
        {
            UiaTextUnit.Character => true,
            UiaTextUnit.Word => text[position - 1] == '\n' || (KindOf(text[position]) != KindOf(text[position - 1]) && !char.IsWhiteSpace(text[position])),
            UiaTextUnit.Line => provider.LineOf(position) != provider.LineOf(position - 1),
            UiaTextUnit.Paragraph => text[position - 1] == '\n',
            _ => false
        };
    }

    private static int KindOf(char symbol) => char.IsWhiteSpace(symbol) ? 0 : char.IsLetterOrDigit(symbol) || symbol == '_' ? 1 : 2;
}
