using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adamantium.UI.Core;

namespace Adamantium.UI.Automation;

internal sealed class ScriptInterpreter
{
    public static readonly string[] Commands =
    [
        "windows", "tree", "visual", "state", "find", "unnamed", "get", "invoke", "toggle", "select", "unselect", "click",
        "rclick",
        "hover", "drag", "expand", "collapse", "scroll", "move", "resize", "zoom", "pan", "context-menu", "drop", "connect", "disconnect", "dock", "window", "set",
        "caret", "type", "key", "wait", "wait-idle", "mark", "errors", "expect", "absent", "shot"
    ];

    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WaitStep = TimeSpan.FromMilliseconds(20);

    private static readonly string[] ElementKeys =
    [
        "name", "id", "type", "class", "value", "toggle", "selected", "expanded", "min", "max", "hscroll", "vscroll",
        "window", "zoom", "dock", "key", "left", "top", "width", "height", "enabled", "offscreen", "focus"
    ];

    private readonly AutomationSession _session;
    private readonly TextWriter _output;

    public ScriptInterpreter(AutomationSession session, TextWriter output)
    {
        _session = session;
        _output = output ?? TextWriter.Null;
    }

    public async Task RunFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new AutomationException($"No scenario at '{path}'.");
        }

        var lines = await File.ReadAllLinesAsync(path);
        for (var number = 1; number <= lines.Length; number++)
        {
            var line = lines[number - 1].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            _output.WriteLine($"> {line}");
            try
            {
                await RunAsync([.. Tokenize(line)]);
            }
            catch (AutomationException e)
            {
                throw new AutomationException($"{e.Message}{Environment.NewLine}{path}:{number}: the scenario stopped here.");
            }
        }

        _output.WriteLine($"{path}: every step passed.");
    }

    public Task RunAsync(IReadOnlyList<string> tokens)
    {
        var arguments = new ScriptArguments(tokens);
        var allowErrors = arguments.Flag("allow-errors");
        return arguments.Command switch
        {
            "windows" => SendAsync(new AutomationRequest { Command = AutomationCommand.Windows }),
            "tree" => TextAsync(AutomationCommand.Tree, arguments),
            "visual" => TextAsync(AutomationCommand.Visual, arguments),
            "state" => TextAsync(AutomationCommand.State, arguments),
            "find" => SendAsync(Target(AutomationCommand.Find, arguments)),
            "unnamed" => SendAsync(Target(AutomationCommand.Unnamed, arguments)),
            "get" => InspectAsync(arguments),
            "invoke" => SendAsync(Target(AutomationCommand.Invoke, arguments, allowErrors)),
            "toggle" => SendAsync(Target(AutomationCommand.Toggle, arguments, allowErrors)),
            "select" => SendAsync(Target(arguments.Flag("add") ? AutomationCommand.AddToSelection : AutomationCommand.Select,
                arguments, allowErrors)),
            "unselect" => SendAsync(Target(AutomationCommand.RemoveFromSelection, arguments, allowErrors)),
            "click" => SendAsync(Target(AutomationCommand.Click, arguments, allowErrors)),
            "rclick" => SendAsync(Target(AutomationCommand.RightClick, arguments, allowErrors)),
            "hover" => SendAsync(Target(AutomationCommand.Hover, arguments, allowErrors)),
            "drag" => SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.Drag,
                Target = arguments.At(0),
                Value = $"{arguments.At(1)} {arguments.At(2)}",
                AllowErrors = allowErrors
            }),
            "expand" => SendAsync(Target(AutomationCommand.Expand, arguments, allowErrors)),
            "collapse" => SendAsync(Target(AutomationCommand.Collapse, arguments, allowErrors)),
            "scroll" => SendAsync(Scroll(arguments, allowErrors)),
            "move" => SendAsync(Pair(AutomationCommand.Move, arguments, allowErrors)),
            "resize" => SendAsync(Pair(AutomationCommand.Resize, arguments, allowErrors)),
            "zoom" => SendAsync(Second(AutomationCommand.Zoom, arguments, allowErrors)),
            "pan" => SendAsync(Pair(AutomationCommand.Pan, arguments, allowErrors)),
            "context-menu" => SendAsync(Target(AutomationCommand.ShowContextMenu, arguments, allowErrors)),
            "drop" => SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.DropOnto,
                Target = arguments.At(0),
                Value = arguments.At(1),
                Properties = arguments.Flag("before") ? ["before"] : arguments.Flag("after") ? ["after"] : null,
                AllowErrors = allowErrors
            }),
            "connect" => SendAsync(Second(AutomationCommand.Connect, arguments, allowErrors)),
            "disconnect" => SendAsync(Second(AutomationCommand.Disconnect, arguments, allowErrors)),
            "dock" => SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.Dock,
                Target = arguments.At(0),
                Value = arguments.At(1),
                Properties = arguments.Option("beside") is { } beside ? [beside] : null,
                AllowErrors = allowErrors
            }),
            "window" => SendAsync(Window(arguments, allowErrors)),
            "set" => SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.SetValue,
                Target = arguments.At(0),
                Value = arguments.At(1),
                AllowErrors = allowErrors
            }),
            "caret" => SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.SelectText,
                Target = arguments.At(0),
                Value = arguments.Option("to") is { } to ? $"{arguments.At(1)}-{to}" : arguments.At(1),
                AllowErrors = allowErrors
            }),
            "type" => SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.Type,
                Target = arguments.Option("into"),
                Value = arguments.At(0),
                AllowErrors = allowErrors
            }),
            "key" => SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.Key,
                Target = arguments.Option("into"),
                Value = string.Join(' ', arguments.From(0)),
                AllowErrors = allowErrors
            }),
            "wait" => arguments.From(1).Any() ? WaitUntilAsync(arguments) : SendAsync(new AutomationRequest
            {
                Command = AutomationCommand.WaitFor,
                Target = arguments.At(0),
                TimeoutMs = (int)(arguments.TimeOption("timeout")?.TotalMilliseconds ?? 0)
            }),
            "wait-idle" => SendAsync(new AutomationRequest { Command = AutomationCommand.WaitIdle }),
            "mark" => MarkAsync(),
            "errors" => ErrorsAsync(arguments),
            "expect" => ExpectAsync(arguments),
            "absent" => AbsentAsync(arguments),
            "shot" => ShotAsync(arguments),
            "run" => throw new AutomationException("A scenario does not run another."),
            null => Task.CompletedTask,
            _ => throw new AutomationException($"No command '{arguments.Command}'.")
        };
    }

    private static AutomationRequest Target(AutomationCommand command, ScriptArguments arguments, bool allowErrors = false) =>
        new() { Command = command, Target = arguments.At(0), AllowErrors = allowErrors };

    private static AutomationRequest Second(AutomationCommand command, ScriptArguments arguments, bool allowErrors) => new()
    {
        Command = command,
        Target = arguments.At(0),
        Value = arguments.At(1),
        AllowErrors = allowErrors
    };

    private static AutomationRequest Pair(AutomationCommand command, ScriptArguments arguments, bool allowErrors) => new()
    {
        Command = command,
        Target = arguments.At(0),
        Value = $"{arguments.At(1)},{arguments.At(2)}",
        AllowErrors = allowErrors
    };

    private static AutomationRequest Scroll(ScriptArguments arguments, bool allowErrors)
    {
        var across = arguments.Option("horizontal");
        var down = arguments.Option("vertical");
        return across == null && down == null
            ? Target(AutomationCommand.ScrollIntoView, arguments, allowErrors)
            : new AutomationRequest
            {
                Command = AutomationCommand.Scroll,
                Target = arguments.At(0),
                Value = $"{across},{down}",
                AllowErrors = allowErrors
            };
    }

    private static AutomationRequest Window(ScriptArguments arguments, bool allowErrors)
    {
        var request = new AutomationRequest { Target = arguments.At(0), AllowErrors = allowErrors };
        switch (arguments.At(1)?.ToLowerInvariant())
        {
            case "minimize":
                request.Command = AutomationCommand.SetWindowState;
                request.Value = nameof(WindowState.Minimized);
                break;
            case "maximize":
                request.Command = AutomationCommand.SetWindowState;
                request.Value = nameof(WindowState.Maximized);
                break;
            case "restore":
                request.Command = AutomationCommand.SetWindowState;
                request.Value = nameof(WindowState.Normal);
                break;
            case "close":
                request.Command = AutomationCommand.Close;
                break;
            default:
                throw new AutomationException("window <selector> minimize | maximize | restore | close");
        }

        return request;
    }

    private async Task TextAsync(AutomationCommand command, ScriptArguments arguments)
    {
        var reply = await _session.RunAsync(new AutomationRequest
        {
            Command = command,
            Target = arguments.At(0),
            Depth = arguments.IntOption("depth")
        });

        if (arguments.Option("out") is { } file)
        {
            await File.WriteAllTextAsync(file, reply.Text);
            _output.WriteLine($"Written to {Path.GetFullPath(file)}.");
        }
        else
        {
            _output.Write(reply.Text);
        }
    }

    private async Task InspectAsync(ScriptArguments arguments)
    {
        var reply = await _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Inspect,
            Target = arguments.At(0),
            Properties = [.. arguments.From(1)]
        });

        _output.WriteLine(ScriptPrinter.Inspection(reply.Details));
    }

    private async Task ShotAsync(ScriptArguments arguments)
    {
        var reply = await _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Shot,
            Target = arguments.At(0),
            Value = arguments.Option("out") ?? "shot.png"
        });

        _output.WriteLine($"Written to {reply.Text}.");
    }

    private async Task MarkAsync()
    {
        var reply = await _session.RunAsync(new AutomationRequest { Command = AutomationCommand.Mark });
        _output.WriteLine(reply.Mark);
    }

    private async Task ErrorsAsync(ScriptArguments arguments)
    {
        var reply = await _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Errors,
            Since = long.TryParse(arguments.Option("since"), out var since) ? since : 0
        });

        foreach (var error in reply.Errors)
        {
            _output.WriteLine(ScriptPrinter.Error(error));
        }

        _output.WriteLine(reply.Errors.Count == 0 ? $"No errors; the mark is {reply.Mark}." : $"The mark is {reply.Mark}.");
    }

    private async Task ExpectAsync(ScriptArguments arguments)
    {
        var expected = Expectations(arguments);
        var (details, misses) = await CompareAsync(arguments.At(0), expected);
        if (misses.Count > 0)
        {
            throw new AutomationException(
                $"{ScriptPrinter.Line(details.Element)} does not match:{Environment.NewLine}{string.Join(Environment.NewLine, misses)}");
        }

        _output.WriteLine($"As expected: {ScriptPrinter.Line(details.Element)}");
    }

    private async Task WaitUntilAsync(ScriptArguments arguments)
    {
        var expected = Expectations(arguments);
        var timeout = arguments.TimeOption("timeout") ?? DefaultWait;
        var clock = Stopwatch.StartNew();
        var last = "it never appeared";
        while (true)
        {
            var found = await _session.SendAsync(new AutomationRequest { Command = AutomationCommand.Find, Target = arguments.At(0) });
            if (found.Ok && found.Elements.Count > 0)
            {
                var (details, misses) = await CompareAsync(arguments.At(0), expected);
                if (misses.Count == 0)
                {
                    _output.WriteLine($"As waited for: {ScriptPrinter.Line(details.Element)}");
                    return;
                }

                last = $"{ScriptPrinter.Line(details.Element)}{Environment.NewLine}{string.Join(Environment.NewLine, misses)}";
            }

            if (clock.Elapsed > timeout)
            {
                throw new AutomationException(
                    $"'{arguments.At(0)}' did not come to that within {timeout.TotalSeconds:0.#} s; last seen: {last}");
            }

            await _session.WaitForIdleAsync();
            await Task.Delay(WaitStep);
        }
    }

    private async Task<(ElementDetails Details, List<string> Misses)> CompareAsync(string target,
        List<KeyValuePair<string, string>> expected)
    {
        var properties = expected.Select(pair => pair.Key).Where(key => !ElementKeys.Contains(key)).ToArray();
        var reply = await _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Inspect,
            Target = target,
            Properties = properties
        });

        var misses = expected
            .Select(pair => (pair.Key, pair.Value, Actual: Actual(reply.Details, pair.Key)))
            .Where(check => check.Actual != check.Value)
            .Select(check => $"  {check.Key}: expected \"{check.Value}\", was \"{check.Actual}\"")
            .ToList();
        return (reply.Details, misses);
    }

    private async Task AbsentAsync(ScriptArguments arguments)
    {
        var reply = await _session.RunAsync(Target(AutomationCommand.Find, arguments));
        if (reply.Elements.Count > 0)
        {
            throw new AutomationException($"'{arguments.At(0)}' should match nothing, and matches:{Environment.NewLine}" +
                                          string.Join(Environment.NewLine, reply.Elements.Select(element => $"  {element.Path}")));
        }

        _output.WriteLine($"Nothing matches '{arguments.At(0)}', as expected.");
    }

    private async Task SendAsync(AutomationRequest request)
    {
        var reply = await _session.RunAsync(request);
        var listing = request.Command is AutomationCommand.Find or AutomationCommand.Unnamed;
        if (reply.Elements == null || reply.Elements.Count == 0)
        {
            _output.WriteLine(request.Command switch
            {
                AutomationCommand.Find => "Nothing matches.",
                AutomationCommand.Unnamed => "Everything that can be acted on has a name.",
                _ => "Done."
            });
        }

        foreach (var element in reply.Elements ?? [])
        {
            _output.WriteLine(listing
                ? $"{ScriptPrinter.Line(element)}{Environment.NewLine}  {element.Path}"
                : ScriptPrinter.Line(element));
        }

        foreach (var error in reply.Errors ?? [])
        {
            _output.WriteLine($"  allowed: {ScriptPrinter.Error(error)}");
        }
    }

    private static List<KeyValuePair<string, string>> Expectations(ScriptArguments arguments)
    {
        var expected = arguments.From(1).Select(Expectation).ToList();
        return expected.Count > 0 ? expected : throw new AutomationException("expect <selector> <key>=<value>...");
    }

    private static KeyValuePair<string, string> Expectation(string text)
    {
        var equals = text.IndexOf('=');
        if (equals <= 0)
        {
            throw new AutomationException($"'{text}': expected key=value.");
        }

        return new KeyValuePair<string, string>(text[..equals], Unquote(text[(equals + 1)..]));
    }

    private static string Actual(ElementDetails details, string key)
    {
        var element = details.Element;
        return key switch
        {
            "name" => element.Name,
            "id" => element.AutomationId,
            "type" => element.ControlType,
            "class" => element.ClassName,
            "value" => element.Value,
            "toggle" => element.ToggleState,
            "selected" => element.IsSelected?.ToString(),
            "expanded" => element.ExpandCollapseState,
            "min" => Invariant(element.Minimum),
            "max" => Invariant(element.Maximum),
            "hscroll" => Invariant(element.HorizontalScroll),
            "vscroll" => Invariant(element.VerticalScroll),
            "window" => element.WindowState,
            "dock" => element.DockPosition,
            "zoom" => Invariant(element.Zoom),
            "key" => element.AccessKey,
            "left" => Invariant(Math.Round(element.Bounds[0])),
            "top" => Invariant(Math.Round(element.Bounds[1])),
            "width" => Invariant(Math.Round(element.Bounds[2])),
            "height" => Invariant(Math.Round(element.Bounds[3])),
            "enabled" => element.IsEnabled.ToString(),
            "offscreen" => element.IsOffscreen.ToString(),
            "focus" => element.HasKeyboardFocus.ToString(),
            _ => Unquote(details.Properties.FirstOrDefault(property => property.Name == key)?.Value)
        };
    }

    public static IEnumerable<string> Tokenize(string line)
    {
        var token = new StringBuilder();
        var quoted = false;
        foreach (var character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length > 0)
                {
                    yield return Unquote(token.ToString());
                    token.Clear();
                }

                continue;
            }

            token.Append(character);
        }

        if (token.Length > 0)
        {
            yield return Unquote(token.ToString());
        }
    }

    private static string Invariant(double? number) => number?.ToString(CultureInfo.InvariantCulture);

    private static string Unquote(string text) =>
        text is { Length: >= 2 } && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
}
