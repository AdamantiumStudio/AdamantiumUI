using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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

    private const string Usage = """
        adam-auto - drives an application that runs the automation agent; by default the sandbox, as an instance of its own.

          start [--tab <Id>] [--theme <Name>] [--exe <path>]   start an instance with the agent and wait for its window
          stop                                                close it
          windows                                             the open windows
          tree [<selector>] [--depth <n>] [--out <file>]      the automation tree
          find <selector>                                     every match, with where it stands
          get <selector>                                      the first match in detail
          invoke | toggle | select | click <selector>         act on the first match
          set <selector> <value>                              write a value
          type <text> [--into <selector>]                     type into the focused element, or into <selector>
          wait <selector> [--timeout 5s]                      wait until something matches
          wait-idle                                           wait until the application has settled

          --pipe <name>   the agent's pipe (default adam-auto)

        A selector: id=Cut   name="Cut out"   type=Button,name=OK   a path: id=Shell/id=Cut
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var arguments = new Arguments(args);
        var pipe = arguments.Option("pipe") ?? DefaultPipe;
        try
        {
            return arguments.Command switch
            {
                "start" => await StartAsync(arguments, pipe),
                "stop" => await StopAsync(pipe),
                "windows" => await SendAsync(pipe, AutomationCommand.Windows),
                "tree" => await TreeAsync(arguments, pipe),
                "find" => await SendAsync(pipe, AutomationCommand.Find, arguments.At(0)),
                "get" => await SendAsync(pipe, AutomationCommand.Get, arguments.At(0), details: true),
                "invoke" => await SendAsync(pipe, AutomationCommand.Invoke, arguments.At(0)),
                "toggle" => await SendAsync(pipe, AutomationCommand.Toggle, arguments.At(0)),
                "select" => await SendAsync(pipe, AutomationCommand.Select, arguments.At(0)),
                "click" => await SendAsync(pipe, AutomationCommand.Click, arguments.At(0)),
                "set" => await SendAsync(pipe, AutomationCommand.SetValue, arguments.At(0), arguments.At(1)),
                "type" => await SendAsync(pipe, AutomationCommand.Type, arguments.Option("into"), arguments.At(0)),
                "wait" => await SendAsync(pipe, AutomationCommand.WaitFor, arguments.At(0),
                    timeout: arguments.TimeOption("timeout")),
                "wait-idle" => await SendAsync(pipe, AutomationCommand.WaitIdle),
                _ => Fail(Usage, 2)
            };
        }
        catch (AutomationException e)
        {
            return Fail(e.Message, 1);
        }
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

    private static async Task<int> TreeAsync(Arguments arguments, string pipe)
    {
        await using var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout);
        var reply = await session.SendAsync(new AutomationRequest
        {
            Command = AutomationCommand.Tree,
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

    private static async Task<int> SendAsync(string pipe, AutomationCommand command, string target = null,
        string value = null, bool details = false, TimeSpan? timeout = null)
    {
        await using var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout);
        var reply = await session.SendAsync(new AutomationRequest
        {
            Command = command,
            Target = target,
            Value = value,
            TimeoutMs = (int)(timeout?.TotalMilliseconds ?? 0)
        });

        if (!reply.Ok)
        {
            return Fail(reply.Error, 1);
        }

        if (reply.Elements == null || reply.Elements.Count == 0)
        {
            Console.WriteLine(command == AutomationCommand.Find ? "Nothing matches." : "Done.");
            return 0;
        }

        foreach (var element in reply.Elements)
        {
            Console.WriteLine(details ? Printer.Details(element) : command == AutomationCommand.Find
                ? $"{Printer.Line(element)}{Environment.NewLine}  {element.Path}"
                : Printer.Line(element));
        }

        return 0;
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

    private static int Fail(string message, int code)
    {
        Console.Error.WriteLine(message);
        return code;
    }
}
