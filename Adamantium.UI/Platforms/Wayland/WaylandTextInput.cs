using System;
using System.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The input method (text-input-v3): text composed in IBus and the like comes as a composition while it is
/// typed and as text once it is chosen, applied a batch at a time on each done.</summary>
internal sealed class WaylandTextInput
{
    private readonly WaylandPlatform platform;
    private readonly ZwpTextInputV3 input;
    private WaylandWindowWorker focused;
    private bool enabled;
    private Rect sentCaret;
    private bool composing;
    private string pendingPreedit;
    private int pendingCursor;
    private string pendingCommit;

    public WaylandTextInput(WaylandPlatform platform, ZwpTextInputManagerV3 manager, WlSeat seat)
    {
        this.platform = platform;
        input = manager.GetTextInput(seat);
        input.Enter += surface =>
        {
            focused = platform.Find(surface);
            Update();
        };
        input.Leave += _ =>
        {
            EndComposition();
            focused = null;
            enabled = false;
            sentCaret = default;
        };
        input.PreeditString += (text, cursorBegin, _) =>
        {
            pendingPreedit = text ?? string.Empty;
            pendingCursor = CharacterIndex(pendingPreedit, cursorBegin);
        };
        input.CommitString += text => pendingCommit = text;
        input.Done += _ => Apply();
    }

    /// <summary>Turns the input method on or off for the window that has the keyboard, as it asks, and tells where its
    /// caret is. Runs on the event thread.</summary>
    public void Update()
    {
        var worker = focused;
        if (worker != null && worker.WantsInputMethod)
        {
            var changed = false;
            if (!enabled)
            {
                input.Enable();
                enabled = true;
                changed = true;
            }

            var caret = worker.Window?.InputMethodCaret ?? default;
            if ((caret.Width > 0 || caret.Height > 0) && caret != sentCaret)
            {
                input.SetCursorRectangle((int)Math.Round(caret.X), (int)Math.Round(caret.Y),
                    Math.Max(1, (int)Math.Round(caret.Width)), Math.Max(1, (int)Math.Round(caret.Height)));
                sentCaret = caret;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            input.Commit();
        }
        else if (enabled)
        {
            EndComposition();
            input.Disable();
            input.Commit();
            enabled = false;
            sentCaret = default;
        }
        else
        {
            return;
        }

        platform.Display.Flush();
    }

    /// <summary>The character a UTF-8 byte offset of <paramref name="text"/> falls on; its end for none.</summary>
    public static int CharacterIndex(string text, int byteOffset)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return byteOffset < 0 || byteOffset > bytes.Length ? text.Length : Encoding.UTF8.GetCharCount(bytes, 0, byteOffset);
    }

    private void Apply()
    {
        var commit = pendingCommit;
        var preedit = pendingPreedit;
        var cursor = pendingCursor;
        pendingCommit = null;
        pendingPreedit = null;

        if (!string.IsNullOrEmpty(commit))
        {
            var text = commit;
            WaylandPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
                new RawTextInputEventArgs(text, KeyboardDevice.CurrentDevice.Modifiers, 0)));
        }

        if (!string.IsNullOrEmpty(preedit))
        {
            if (!composing)
            {
                composing = true;
                Compose(RawTextCompositionEventType.Started, string.Empty, 0);
            }

            Compose(RawTextCompositionEventType.Changed, preedit, cursor);
        }
        else
        {
            EndComposition();
        }

        if (enabled)
        {
            Update();
        }
    }

    private void EndComposition()
    {
        if (!composing)
        {
            return;
        }

        composing = false;
        Compose(RawTextCompositionEventType.Ended, string.Empty, 0);
    }

    private static void Compose(RawTextCompositionEventType type, string text, int cursor) =>
        WaylandPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawTextCompositionEventArgs(type, text, cursor, KeyboardDevice.CurrentDevice.Modifiers, 0)));
}
