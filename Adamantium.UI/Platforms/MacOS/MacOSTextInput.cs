using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.MacOS.Foundation;
using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>Where what the input method makes of the keys goes: what it composes, what it types, and the keys it has no use
/// for. The input method is told of the text typed since the caret was last moved - what it may take back, as the letter
/// held down does for its accents.</summary>
internal sealed class MacOSTextInput(ITextInputTarget target)
{
    private const int MostRemembered = 256;

    private readonly List<Action> _made = [];
    private string _typed = string.Empty;
    private string _composed = string.Empty;
    private NSRange _composedSelection;
    private bool _isComposing;
    private bool _inKey;
    private bool _wasTold;
    private bool _wasCommanded;
    private bool _replaced;

    /// <summary>The text told to the input method: what was typed, and what is being composed after it.</summary>
    internal string Text => _typed + _composed;

    /// <summary>Where what is composed stands in <see cref="Text"/>.</summary>
    internal NSRange MarkedRange => _composed.Length > 0 ? new NSRange(_typed.Length, _composed.Length) : NSRange.None;

    /// <summary>Where the caret is in <see cref="Text"/>.</summary>
    internal NSRange SelectedRange => _composed.Length > 0
        ? new NSRange(_typed.Length + _composedSelection.Location, _composedSelection.Length)
        : new NSRange(_typed.Length, 0);

    internal bool IsComposing => _isComposing;

    /// <summary>Hands a key to the input method - <paramref name="interpret"/> - and reports what it made of it, with the
    /// key itself - <paramref name="reportKey"/> - unless the input method kept the key: it works on what is composed, or
    /// chooses among the accents of a letter held down. False when it has no use for the key at all.</summary>
    internal bool Press(Func<bool> interpret, Action reportKey)
    {
        var wasComposing = _isComposing;
        _inKey = true;
        _wasTold = false;
        _wasCommanded = false;
        _replaced = false;
        bool taken;
        try
        {
            taken = interpret();
        }
        finally
        {
            _inKey = false;
        }

        if (!wasComposing && (_wasTold ? !_replaced : _wasCommanded || !taken))
            reportKey();

        foreach (var report in _made)
            report();
        _made.Clear();

        // One that ended the composition as well as doing its own - an arrow after a dead key - does its own after it.
        if (wasComposing && (!taken || _wasCommanded && !_isComposing))
            reportKey();

        if (_wasCommanded)
            _typed = string.Empty;
        return taken;
    }

    /// <summary>The caret went elsewhere: what was typed is no longer before it.</summary>
    internal void ForgetTyped() => _typed = string.Empty;

    /// <summary>Text typed; <paramref name="replaced"/>, in <see cref="Text"/>, is what it takes the place of, none for
    /// what is composed.</summary>
    internal void Insert(string text, NSRange replaced)
    {
        _wasTold = true;
        Erase(replaced);
        Remember(text);
        if (!string.IsNullOrEmpty(text))
            Report(() => target.Type(text));
        EndComposing();
    }

    /// <summary>Text being composed, with what is selected of it.</summary>
    internal void Compose(string text, NSRange selected, NSRange replaced)
    {
        _wasTold = true;
        Erase(replaced);
        if (string.IsNullOrEmpty(text))
        {
            EndComposing();
            return;
        }

        _composed = text;
        var start = (nint)Math.Clamp(selected.Location, 0, text.Length);
        _composedSelection = new NSRange(start, Math.Clamp(selected.Length, 0, text.Length - start));
        if (!_isComposing)
        {
            _isComposing = true;
            Report(() => target.Compose(RawTextCompositionEventType.Started, string.Empty, 0));
        }

        var cursor = (int)_composedSelection.End;
        Report(() => target.Compose(RawTextCompositionEventType.Changed, text, cursor));
    }

    /// <summary>What is composed is typed as it is.</summary>
    internal void Commit()
    {
        if (_isComposing)
            Insert(_composed, NSRange.None);
    }

    /// <summary>What is composed is let go of.</summary>
    internal void Discard() => EndComposing();

    /// <summary>The input method has no use for the key but as a command.</summary>
    internal void Command() => _wasCommanded = true;

    /// <summary>A part of <see cref="Text"/>, and which; null where there is none.</summary>
    internal string Part(NSRange range, out NSRange actual)
    {
        var text = Text;
        actual = NSRange.None;
        if (!range.IsFound || range.Location < 0 || range.Location > text.Length)
            return null;

        actual = new NSRange(range.Location, Math.Clamp(range.Length, 0, text.Length - range.Location));
        return actual.Length > 0 ? text.Substring((int)actual.Location, (int)actual.Length) : null;
    }

    // What was typed is taken back by Backspace, as it was typed by its keys. Only its end is before the caret.
    private void Erase(NSRange replaced)
    {
        if (!replaced.IsFound || replaced.Length <= 0 || _composed.Length > 0 || replaced.Location < 0 ||
            replaced.End != _typed.Length)
        {
            return;
        }

        var count = new StringInfo(_typed[(int)replaced.Location..]).LengthInTextElements;
        _typed = _typed[..(int)replaced.Location];
        _replaced = _inKey;
        Report(() => target.Erase(count));
    }

    private void EndComposing()
    {
        _composed = string.Empty;
        if (!_isComposing)
            return;

        _isComposing = false;
        Report(() => target.Compose(RawTextCompositionEventType.Ended, string.Empty, 0));
    }

    private void Remember(string text)
    {
        _typed += text;
        if (_typed.Length > MostRemembered)
            _typed = string.Empty;
    }

    private void Report(Action report)
    {
        if (_inKey)
            _made.Add(report);
        else
            report();
    }
}
