using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Automation.Cli;

internal static class Commands
{
    public static readonly string[] Names =
    [
        "windows", "tree", "visual", "state", "find", "unnamed", "get", "invoke", "toggle", "select", "unselect", "click",
        "rclick", "hover", "wheel", "drag", "touch", "touches", "pinch", "pen", "expand", "collapse", "scroll", "move",
        "resize", "zoom", "pan", "context-menu", "drop", "connect", "disconnect", "dock", "window", "set", "caret", "type",
        "key", "wait", "wait-idle", "mark", "errors", "expect", "absent", "shot", "memory", "theme"
    ];

    public static async Task RunAsync(AutomationSession session, ScriptArguments arguments, TextWriter output)
    {
        var sessionWide = arguments.Command is "windows" or "state" or "wait-idle" or "mark" or "errors" or "type" or "key" or "touches"
            or "memory" or "theme";
        if (!sessionWide && arguments.At(0) == null && arguments.Command is not ("tree" or "unnamed" or "shot"))
        {
            throw new AutomationException($"{arguments.Command} needs a selector.");
        }

        var target = sessionWide || arguments.At(0) == null ? null : session.Find(arguments.At(0));
        switch (arguments.Command)
        {
            case "windows":
                Print(output, await session.WindowsAsync(), false);
                return;
            case "tree":
                await WriteAsync(output, arguments, target == null ? await session.TreeAsync(arguments.IntOption("depth"))
                    : await target.TreeAsync(arguments.IntOption("depth")));
                return;
            case "visual":
                await WriteAsync(output, arguments, await target.VisualAsync(arguments.IntOption("depth")));
                return;
            case "state":
                await WriteAsync(output, arguments, await session.StateAsync());
                return;
            case "find":
                Print(output, await target.FindAllAsync(), true, "Nothing matches.");
                return;
            case "unnamed":
                Print(output, await session.UnnamedAsync(arguments.At(0)), true, "Everything that can be acted on has a name.");
                return;
            case "get":
                output.WriteLine(ScriptPrinter.Inspection(await target.InspectAsync([.. arguments.From(1)])));
                return;
            case "wait-idle":
                await session.WaitForIdleAsync();
                output.WriteLine("Done.");
                return;
            case "mark":
                output.WriteLine(await session.MarkAsync());
                return;
            case "memory":
                output.WriteLine(await session.MemoryAsync());
                return;
            case "theme":
                output.WriteLine(await session.ThemeAsync(arguments.At(0), arguments.Option("variant")));
                return;
            case "errors":
                await ErrorsAsync(session, arguments, output);
                return;
            case "expect":
                output.WriteLine($"As expected: {ScriptPrinter.Line((await target.ExpectAsync([.. Expectations(arguments)])).Element)}");
                return;
            case "absent":
                await target.ExpectAbsentAsync();
                output.WriteLine($"Nothing matches '{arguments.At(0)}', as expected.");
                return;
            case "wait":
                output.WriteLine(arguments.From(1).Any()
                    ? $"As waited for: {ScriptPrinter.Line((await target.WaitUntilAsync([.. Expectations(arguments)], arguments.TimeOption("timeout"))).Element)}"
                    : ScriptPrinter.Line(await target.WaitForAsync(arguments.TimeOption("timeout"))));
                return;
            case "shot":
                output.WriteLine($"Written to {await (target == null
                    ? session.ShotAsync(arguments.Option("out") ?? "shot.png")
                    : target.ShotAsync(arguments.Option("out") ?? "shot.png"))}.");
                return;
        }

        var mark = await session.MarkAsync();
        session.AllowErrors = arguments.Flag("allow-errors");
        await ActAsync(session, target, arguments);
        output.WriteLine("Done.");
        if (session.AllowErrors)
        {
            foreach (var error in await session.ErrorsSinceAsync(mark))
            {
                output.WriteLine($"  allowed: {ScriptPrinter.Error(error)}");
            }
        }
    }

    private static Task ActAsync(AutomationSession session, AutomationElement target, ScriptArguments arguments) => arguments.Command switch
    {
        "invoke" => target.InvokeAsync(),
        "toggle" => target.ToggleAsync(),
        "select" => arguments.Flag("add") ? target.AddToSelectionAsync() : target.SelectAsync(),
        "unselect" => target.RemoveFromSelectionAsync(),
        "click" => target.ClickAsync(),
        "rclick" => target.RightClickAsync(),
        "hover" => target.HoverAsync(),
        "expand" => target.ExpandAsync(),
        "collapse" => target.CollapseAsync(),
        "context-menu" => target.ShowContextMenuAsync(),
        "set" => target.SetValueAsync(arguments.At(1)),
        "caret" => Caret(target, arguments),
        "type" => arguments.Option("into") is { } into ? session.Find(into).TypeAsync(arguments.At(0)) : session.TypeAsync(arguments.At(0)),
        "key" => arguments.Option("into") is { } keysInto
            ? session.Find(keysInto).PressKeysAsync(string.Join(' ', arguments.From(0)))
            : session.PressKeysAsync(string.Join(' ', arguments.From(0))),
        "scroll" => arguments.Option("horizontal") == null && arguments.Option("vertical") == null
            ? target.ScrollIntoViewAsync()
            : target.ScrollToAsync(Number(arguments.Option("horizontal")), Number(arguments.Option("vertical"))),
        "move" => target.MoveByAsync(Double(arguments.At(1)), Double(arguments.At(2))),
        "resize" => target.ResizeAsync(Double(arguments.At(1)), Double(arguments.At(2))),
        "pan" => target.PanAsync(Double(arguments.At(1)), Double(arguments.At(2))),
        "zoom" => target.ZoomAsync(Double(arguments.At(1))),
        "wheel" => target.WheelAsync(int.TryParse(arguments.At(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var notches)
            ? notches
            : throw new AutomationException($"'{arguments.At(1)}': expected a whole number of notches.")),
        "drag" => DragAsync(target.DragAsync, arguments.At(1), arguments.At(2)),
        "drop" => target.DropOntoAsync(session.Find(arguments.At(1)),
            arguments.Flag("before") ? DropSide.Before : arguments.Flag("after") ? DropSide.After : DropSide.Onto),
        "connect" => target.ConnectAsync(session.Find(arguments.At(1))),
        "disconnect" => target.DisconnectAsync(arguments.At(1) is { } other ? session.Find(other) : null),
        "dock" => target.DockAsync(Enum.TryParse<DockPosition>(arguments.At(1), true, out var position)
                ? position
                : throw new AutomationException("dock <pane> top | left | bottom | right | fill | none"),
            arguments.Option("beside") is { } beside ? session.Find(beside) : null),
        "window" => Window(target, arguments.At(1)),
        "touch" => Touch(target, arguments),
        "pen" => Pen(target, arguments),
        "touches" => Touches(session, arguments),
        "pinch" => Pinch(target, arguments),
        _ => throw new AutomationException($"No command '{arguments.Command}'.")
    };

    private static Task Caret(AutomationElement target, ScriptArguments arguments)
    {
        var (line, column) = TextPosition(arguments.At(1));
        if (arguments.Option("to") is not { } to)
        {
            return target.PlaceCaretAsync(line, column);
        }

        var (toLine, toColumn) = TextPosition(to);
        return target.SelectTextAsync(line, column, toLine, toColumn);
    }

    private static Task Window(AutomationElement target, string action) => action?.ToLowerInvariant() switch
    {
        "minimize" => target.SetWindowStateAsync(WindowState.Minimized),
        "maximize" => target.SetWindowStateAsync(WindowState.Maximized),
        "restore" => target.SetWindowStateAsync(WindowState.Normal),
        "close" => target.CloseAsync(),
        "hide" => target.HideAsync(),
        "show" => target.ShowAsync(),
        _ => throw new AutomationException("window <selector> minimize | maximize | restore | close | hide | show")
    };

    private static Task Touch(AutomationElement target, ScriptArguments arguments)
    {
        var system = arguments.Flag("os");
        return arguments.At(1) switch
        {
            "tap" => target.TapAsync(system),
            "hold" => target.HoldAsync(system),
            "drag" => DragAsync((fromX, fromY, toX, toY) => target.TouchDragAsync(fromX, fromY, toX, toY, system), arguments.At(2), arguments.At(3)),
            var gesture => throw new AutomationException($"'{gesture}': tap, hold, or drag x1,y1 x2,y2.")
        };
    }

    private static Task Pen(AutomationElement target, ScriptArguments arguments)
    {
        var system = arguments.Flag("os");
        var barrel = arguments.Flag("barrel");
        var eraser = arguments.Flag("eraser");
        var pressure = arguments.Option("pressure")?.Split("..");
        var single = pressure == null ? 0.5f : Float(pressure[0]);
        return arguments.At(1) switch
        {
            "tap" => target.PenTapAsync(single, barrel, eraser, system),
            "hold" => target.PenHoldAsync(single, system),
            "drag" when pressure is { Length: 2 } => DragAsync((fromX, fromY, toX, toY) =>
                target.PenDragAsync(fromX, fromY, toX, toY, Float(pressure[0]), Float(pressure[1]), barrel, eraser, system), arguments.At(2), arguments.At(3)),
            "drag" => DragAsync((fromX, fromY, toX, toY) =>
                target.PenDragAsync(fromX, fromY, toX, toY, single, barrel, eraser, system), arguments.At(2), arguments.At(3)),
            var gesture => throw new AutomationException($"'{gesture}': tap, hold, or drag x1,y1 x2,y2.")
        };
    }

    private static Task Touches(AutomationSession session, ScriptArguments arguments)
    {
        var words = arguments.From(0).ToList();
        var at = words.FindIndex(word => word is "tap" or "hold" or "drag");
        if (at < 1)
        {
            throw new AutomationException("touches <selector>... tap | hold | drag dx,dy: the elements, then the gesture.");
        }

        var fingers = words.Take(at).Select(session.Find).ToList();
        var system = arguments.Flag("os");
        switch (words[at])
        {
            case "tap":
                return session.TapTogetherAsync(fingers, system);
            case "hold":
                return session.HoldTogetherAsync(fingers, system);
            default:
                var (dx, dy) = Pair(words.Count > at + 1 ? words[at + 1] : null);
                return session.TouchDragTogetherAsync(fingers, dx, dy, system);
        }
    }

    private static Task Pinch(AutomationElement target, ScriptArguments arguments)
    {
        var (panX, panY) = arguments.Option("pan") is { } pan ? Pair(pan) : (0, 0);
        return target.PinchAsync(Double(arguments.At(1)), Number(arguments.Option("rotate")) ?? 0, panX, panY, arguments.Flag("os"));
    }

    private static async Task ErrorsAsync(AutomationSession session, ScriptArguments arguments, TextWriter output)
    {
        var since = long.TryParse(arguments.Option("since"), out var mark) ? mark : 0;
        var errors = await session.ErrorsSinceAsync(since);
        foreach (var error in errors)
        {
            output.WriteLine(ScriptPrinter.Error(error));
        }

        var newest = await session.MarkAsync();
        output.WriteLine(errors.Count == 0 ? $"No errors; the mark is {newest}." : $"The mark is {newest}.");
    }

    private static IEnumerable<Expectation> Expectations(ScriptArguments arguments)
    {
        var expected = arguments.From(1).Select(text =>
        {
            var equals = text.IndexOf('=');
            return equals > 0
                ? new Expectation(text[..equals], Unquote(text[(equals + 1)..]))
                : throw new AutomationException($"'{text}': expected key=value.");
        }).ToList();
        return expected.Count > 0 ? expected : throw new AutomationException("expect <selector> <key>=<value>...");
    }

    private static Task DragAsync(Func<double, double, double, double, Task> drag, string from, string to)
    {
        var (fromX, fromY) = Pair(from);
        var (toX, toY) = Pair(to);
        return drag(fromX, fromY, toX, toY);
    }

    private static async Task WriteAsync(TextWriter output, ScriptArguments arguments, string text)
    {
        if (arguments.Option("out") is { } file)
        {
            await File.WriteAllTextAsync(file, text);
            output.WriteLine($"Written to {Path.GetFullPath(file)}.");
        }
        else
        {
            output.Write(text);
        }
    }

    private static void Print(TextWriter output, IReadOnlyList<ElementInfo> elements, bool withPath, string none = null)
    {
        if (elements.Count == 0 && none != null)
        {
            output.WriteLine(none);
        }

        foreach (var element in elements)
        {
            output.WriteLine(withPath ? $"{ScriptPrinter.Line(element)}{Environment.NewLine}  {element.Path}" : ScriptPrinter.Line(element));
        }
    }

    private static (double X, double Y) Pair(string text)
    {
        var parts = text?.Split(',');
        return parts is { Length: 2 }
            ? (Double(parts[0]), Double(parts[1]))
            : throw new AutomationException($"'{text}': expected x,y.");
    }

    private static (int Line, int Column) TextPosition(string text)
    {
        var parts = text?.Split(':') ?? [];
        return parts.Length is 1 or 2 && int.TryParse(parts[0], out var line)
            ? (line, parts.Length > 1 && int.TryParse(parts[1], out var column) ? column : 1)
            : throw new AutomationException($"'{text}': expected line[:column].");
    }

    private static double Double(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        ? number
        : throw new AutomationException($"'{text}': expected a number.");

    private static double? Number(string text) => text == null ? null : Double(text);

    private static float Float(string text) => (float)Double(text);

    private static string Unquote(string text) =>
        text is { Length: >= 2 } && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
}
