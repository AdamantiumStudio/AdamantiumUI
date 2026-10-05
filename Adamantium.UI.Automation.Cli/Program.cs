using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Automation.Cli;

public static class Program
{
    private const string DefaultPipe = "adam-auto";
    private const string DefaultApplication = "Adamantium.UI.Sandbox.exe";
    private const string GalleryTabs = "GalleryTabs";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(300);

    private static readonly string[] ElementKeys =
    [
        "name", "id", "type", "class", "value", "toggle", "selected", "expanded", "min", "max", "hscroll", "vscroll",
        "window", "key", "enabled", "offscreen", "focus"
    ];

    private const string Usage = """
        adam-auto - drives an application that runs the automation agent; by default the sandbox, as an instance of its own.

          start [--tab <Id>] [--theme <Name>] [--exe <path>]   start an instance with the agent and wait for its window
          stop                                                close it
          windows                                             the open windows
          tree [<selector>] [--depth <n>] [--out <file>]      the automation tree
          visual <selector> [--depth <n>] [--out <file>]      the visual tree under it, with layout
          find <selector>                                     every match, with where it stands
          unnamed [<selector>]                                what can be acted on but has no name
          get <selector> [<property>...]                      the first match: properties with their source, bindings, layout
          invoke | toggle | select | expand | collapse <selector>   act on the first match by what it can do
          click | rclick | hover <selector>                   or by input made inside the application
          scroll <selector>                                   bring a list's item into view, making its element
          scroll <selector> [--vertical <%>] [--horizontal <%>]   scroll a list or a scroll viewer to percents
          window <selector> minimize | maximize | restore | close
          set <selector> <value>                              write a value: text, or a number
          type <text> [--into <selector>]                     type into the focused element, or into <selector>
          key <keys>... [--into <selector>]                   press keys: Alt, Ctrl+S, Shift+Tab, F, Enter... in the
                                                              focused window, or in <selector>'s (focusing it if it can)
          wait <selector> [--timeout 5s]                      wait until something matches
          wait-idle                                           wait until the application has settled
          state                                               keyboard focus, windows, open popups
          shot [<selector>] [--out shot.png]                  a picture of it, or of the first window, to look at
          mark | errors [--since <mark>]                      the error journal: its newest entry, what came after a mark
          expect <selector> <key>=<value>...                  fail unless it matches: name, id, type, class, value, toggle,
                                                              selected, expanded, min, max, hscroll, vscroll, window, key,
                                                              enabled, offscreen, focus, or a property name
          absent <selector>                                   fail if anything matches
          run <scenario>                                      the commands of a file, one a line, up to the first failure

          --pipe <name>     the agent's pipe (default adam-auto)
          --allow-errors    an action may leave errors in the journal; by default that fails it

        A selector: id=Cut   name="Cut out"   type=Button,name=OK   a path: id=Shell/id=Cut
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return await RunAsync(new Arguments(args), DefaultPipe);
    }

    private static async Task<int> RunAsync(Arguments arguments, string defaultPipe)
    {
        var pipe = arguments.Option("pipe") ?? defaultPipe;
        var allowErrors = arguments.Flag("allow-errors");
        try
        {
            return arguments.Command switch
            {
                "start" => await StartAsync(arguments, pipe),
                "stop" => await StopAsync(pipe),
                "windows" => await SendAsync(pipe, new AutomationRequest { Command = AutomationCommand.Windows }),
                "tree" => await TextAsync(pipe, AutomationCommand.Tree, arguments),
                "visual" => await TextAsync(pipe, AutomationCommand.Visual, arguments),
                "state" => await TextAsync(pipe, AutomationCommand.State, arguments),
                "find" => await SendAsync(pipe, Target(AutomationCommand.Find, arguments)),
                "unnamed" => await SendAsync(pipe, Target(AutomationCommand.Unnamed, arguments)),
                "get" => await InspectAsync(pipe, arguments),
                "invoke" => await SendAsync(pipe, Target(AutomationCommand.Invoke, arguments, allowErrors)),
                "toggle" => await SendAsync(pipe, Target(AutomationCommand.Toggle, arguments, allowErrors)),
                "select" => await SendAsync(pipe, Target(AutomationCommand.Select, arguments, allowErrors)),
                "click" => await SendAsync(pipe, Target(AutomationCommand.Click, arguments, allowErrors)),
                "rclick" => await SendAsync(pipe, Target(AutomationCommand.RightClick, arguments, allowErrors)),
                "hover" => await SendAsync(pipe, Target(AutomationCommand.Hover, arguments, allowErrors)),
                "expand" => await SendAsync(pipe, Target(AutomationCommand.Expand, arguments, allowErrors)),
                "collapse" => await SendAsync(pipe, Target(AutomationCommand.Collapse, arguments, allowErrors)),
                "scroll" => await SendAsync(pipe, Scroll(arguments, allowErrors)),
                "window" => await SendAsync(pipe, Window(arguments, allowErrors)),
                "set" => await SendAsync(pipe, new AutomationRequest
                {
                    Command = AutomationCommand.SetValue,
                    Target = arguments.At(0),
                    Value = arguments.At(1),
                    AllowErrors = allowErrors
                }),
                "type" => await SendAsync(pipe, new AutomationRequest
                {
                    Command = AutomationCommand.Type,
                    Target = arguments.Option("into"),
                    Value = arguments.At(0),
                    AllowErrors = allowErrors
                }),
                "key" => await SendAsync(pipe, new AutomationRequest
                {
                    Command = AutomationCommand.Key,
                    Target = arguments.Option("into"),
                    Value = string.Join(' ', arguments.From(0)),
                    AllowErrors = allowErrors
                }),
                "wait" => await SendAsync(pipe, new AutomationRequest
                {
                    Command = AutomationCommand.WaitFor,
                    Target = arguments.At(0),
                    TimeoutMs = (int)(arguments.TimeOption("timeout")?.TotalMilliseconds ?? 0)
                }),
                "wait-idle" => await SendAsync(pipe, new AutomationRequest { Command = AutomationCommand.WaitIdle }),
                "mark" => await MarkAsync(pipe),
                "errors" => await ErrorsAsync(pipe, arguments),
                "expect" => await ExpectAsync(pipe, arguments),
                "absent" => await AbsentAsync(pipe, arguments),
                "shot" => await ShotAsync(pipe, arguments),
                "run" => await ScenarioAsync(arguments, pipe),
                _ => Fail(Usage, 2)
            };
        }
        catch (AutomationException e)
        {
            return Fail(e.Message, 1);
        }
    }

    private static AutomationRequest Target(AutomationCommand command, Arguments arguments, bool allowErrors = false) =>
        new() { Command = command, Target = arguments.At(0), AllowErrors = allowErrors };

    private static AutomationRequest Scroll(Arguments arguments, bool allowErrors)
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

    private static AutomationRequest Window(Arguments arguments, bool allowErrors)
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

    private static async Task<int> StartAsync(Arguments arguments, string pipe)
    {
        if (await AnswersAsync(pipe))
        {
            return Fail($"An instance already answers on pipe '{pipe}'. Close it first: adam-auto stop", 1);
        }

        var exe = Path.GetFullPath(arguments.Option("exe") ?? Path.Combine(AppContext.BaseDirectory, DefaultApplication));
        if (!File.Exists(exe))
        {
            return Fail($"No application at {exe}; build it, or name one with --exe.", 1);
        }

        Environment.SetEnvironmentVariable(AutomationProtocol.PipeVariable, pipe);
        if (arguments.Option("theme") is { } theme)
        {
            Environment.SetEnvironmentVariable("ADAM_THEME", theme);
        }

        using var process = Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)
        });

        await using var session = await AutomationSession.AttachAsync(pipe, StartTimeout);
        var window = await session.Find(By.Type(AutomationControlType.Window)).WaitForAsync(StartTimeout);
        if (arguments.Option("tab") is { } tab)
        {
            await session.Find(By.Id(GalleryTabs)).Find(By.Id(tab)).SelectAsync();
        }

        Console.WriteLine($"Started {Path.GetFileName(exe)} (process {process?.Id}) on pipe '{pipe}'.");
        Console.WriteLine(Printer.Line(window));
        return 0;
    }

    private static async Task<int> StopAsync(string pipe)
    {
        await using var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout);
        await session.SendAsync(new AutomationRequest { Command = AutomationCommand.Shutdown });
        Console.WriteLine("Closed.");
        return 0;
    }

    private static async Task<int> TextAsync(string pipe, AutomationCommand command, Arguments arguments)
    {
        var reply = await ReplyAsync(pipe, new AutomationRequest
        {
            Command = command,
            Target = arguments.At(0),
            Depth = arguments.IntOption("depth")
        });

        if (!reply.Ok)
        {
            return Fail(reply.Error, 1);
        }

        if (arguments.Option("out") is { } file)
        {
            await File.WriteAllTextAsync(file, reply.Text);
            Console.WriteLine($"Written to {Path.GetFullPath(file)}.");
        }
        else
        {
            Console.Write(reply.Text);
        }

        return 0;
    }

    private static async Task<int> InspectAsync(string pipe, Arguments arguments)
    {
        var reply = await ReplyAsync(pipe, new AutomationRequest
        {
            Command = AutomationCommand.Inspect,
            Target = arguments.At(0),
            Properties = [.. arguments.From(1)]
        });

        if (!reply.Ok)
        {
            return Fail(reply.Error, 1);
        }

        Console.WriteLine(Printer.Inspection(reply.Details));
        return 0;
    }

    private static async Task<int> ShotAsync(string pipe, Arguments arguments)
    {
        var reply = await ReplyAsync(pipe, new AutomationRequest
        {
            Command = AutomationCommand.Shot,
            Target = arguments.At(0),
            Value = arguments.Option("out") ?? "shot.png"
        });

        if (!reply.Ok)
        {
            return Fail(reply.Error, 1);
        }

        Console.WriteLine($"Written to {reply.Text}.");
        return 0;
    }

    private static async Task<int> MarkAsync(string pipe)
    {
        var reply = await ReplyAsync(pipe, new AutomationRequest { Command = AutomationCommand.Mark });
        Console.WriteLine(reply.Mark);
        return 0;
    }

    private static async Task<int> ErrorsAsync(string pipe, Arguments arguments)
    {
        var reply = await ReplyAsync(pipe, new AutomationRequest
        {
            Command = AutomationCommand.Errors,
            Since = long.TryParse(arguments.Option("since"), out var since) ? since : 0
        });

        foreach (var error in reply.Errors)
        {
            Console.WriteLine(Printer.Error(error));
        }

        Console.WriteLine(reply.Errors.Count == 0 ? $"No errors; the mark is {reply.Mark}." : $"The mark is {reply.Mark}.");
        return 0;
    }

    private static async Task<int> ExpectAsync(string pipe, Arguments arguments)
    {
        var expected = arguments.From(1).Select(Expectation).ToList();
        if (expected.Count == 0)
        {
            return Fail("expect <selector> <key>=<value>...", 2);
        }

        var properties = expected.Select(pair => pair.Key).Where(key => !ElementKeys.Contains(key)).ToArray();
        var reply = await ReplyAsync(pipe, new AutomationRequest
        {
            Command = AutomationCommand.Inspect,
            Target = arguments.At(0),
            Properties = properties
        });

        if (!reply.Ok)
        {
            return Fail(reply.Error, 1);
        }

        var misses = expected
            .Select(pair => (pair.Key, pair.Value, Actual: Actual(reply.Details, pair.Key)))
            .Where(check => check.Actual != check.Value)
            .Select(check => $"  {check.Key}: expected \"{check.Value}\", was \"{check.Actual}\"")
            .ToList();

        if (misses.Count > 0)
        {
            return Fail($"{Printer.Line(reply.Details.Element)} does not match:{Environment.NewLine}{string.Join(Environment.NewLine, misses)}", 1);
        }

        Console.WriteLine($"As expected: {Printer.Line(reply.Details.Element)}");
        return 0;
    }

    private static async Task<int> AbsentAsync(string pipe, Arguments arguments)
    {
        var reply = await ReplyAsync(pipe, Target(AutomationCommand.Find, arguments));
        if (!reply.Ok)
        {
            return Fail(reply.Error, 1);
        }

        if (reply.Elements.Count > 0)
        {
            return Fail($"'{arguments.At(0)}' should match nothing, and matches:{Environment.NewLine}" +
                        string.Join(Environment.NewLine, reply.Elements.Select(element => $"  {element.Path}")), 1);
        }

        Console.WriteLine($"Nothing matches '{arguments.At(0)}', as expected.");
        return 0;
    }

    private static async Task<int> ScenarioAsync(Arguments arguments, string pipe)
    {
        var file = arguments.At(0);
        if (file == null || !File.Exists(file))
        {
            return Fail($"No scenario at '{file}'.", 2);
        }

        var lines = await File.ReadAllLinesAsync(file);
        for (var number = 1; number <= lines.Length; number++)
        {
            var line = lines[number - 1].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            Console.WriteLine($"> {line}");
            var step = new Arguments([.. Tokenize(line)]);
            if (step.Command == "run")
            {
                return Fail($"{file}:{number}: a scenario does not run another.", 2);
            }

            if (await RunAsync(step, pipe) != 0)
            {
                return Fail($"{file}:{number}: the scenario stopped here.", 1);
            }
        }

        Console.WriteLine($"{file}: every step passed.");
        return 0;
    }

    private static async Task<int> SendAsync(string pipe, AutomationRequest request)
    {
        var reply = await ReplyAsync(pipe, request);
        if (!reply.Ok)
        {
            return Fail(reply.Error, 1);
        }

        var listing = request.Command is AutomationCommand.Find or AutomationCommand.Unnamed;
        if (reply.Elements == null || reply.Elements.Count == 0)
        {
            Console.WriteLine(request.Command switch
            {
                AutomationCommand.Find => "Nothing matches.",
                AutomationCommand.Unnamed => "Everything that can be acted on has a name.",
                _ => "Done."
            });
        }

        foreach (var element in reply.Elements ?? [])
        {
            Console.WriteLine(listing
                ? $"{Printer.Line(element)}{Environment.NewLine}  {element.Path}"
                : Printer.Line(element));
        }

        foreach (var error in reply.Errors ?? [])
        {
            Console.WriteLine($"  allowed: {Printer.Error(error)}");
        }

        return 0;
    }

    private static async Task<AutomationReply> ReplyAsync(string pipe, AutomationRequest request)
    {
        await using var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout);
        return await session.SendAsync(request);
    }

    private static async Task<bool> AnswersAsync(string pipe)
    {
        try
        {
            await using var session = await AutomationSession.AttachAsync(pipe, ProbeTimeout);
            return true;
        }
        catch (AutomationException)
        {
            return false;
        }
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
            "key" => element.AccessKey,
            "enabled" => element.IsEnabled.ToString(),
            "offscreen" => element.IsOffscreen.ToString(),
            "focus" => element.HasKeyboardFocus.ToString(),
            _ => Unquote(details.Properties.FirstOrDefault(property => property.Name == key)?.Value)
        };
    }

    private static IEnumerable<string> Tokenize(string line)
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

    private static int Fail(string message, int code)
    {
        Console.Error.WriteLine(message);
        return code;
    }
}
