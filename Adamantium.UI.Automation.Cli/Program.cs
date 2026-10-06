using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        adam-auto - drives an application that runs the automation agent (it calls UseAutomationAgent()), as an instance
        of its own.

          start --exe <path> [--tab <Id>] [--theme <Name>]   start an instance with the agent and wait for its window;
                                                              beside the sandbox, the sandbox when no --exe is named
          stop                                                close it
          windows                                             the open windows
          tree [<selector>] [--depth <n>] [--out <file>]      the automation tree
          visual <selector> [--depth <n>] [--out <file>]      the visual tree under it, with layout
          find <selector>                                     every match, with where it stands
          unnamed [<selector>]                                what can be acted on but has no name
          get <selector> [<property>...]                      the first match: properties with their source, bindings, layout
          invoke | toggle | select | expand | collapse <selector>   act on the first match by what it can do
          select <selector> --add | unselect <selector>       add to a selection of many, or take out of it
          click | rclick | hover <selector>                   or by input made inside the application
          drag <selector> <x1,y1> <x2,y2>                     a left-button drag across it, in its own units
          drop <selector> <target> [--before|--after]         carry it onto another element and let it go there
          scroll <selector>                                   bring a list's item into view, making its element
          scroll <selector> [--vertical <%>] [--horizontal <%>]   scroll a list or a scroll viewer to percents
          move <selector> <dx> <dy>                           move a splitter or a canvas node, in its own units
          resize <selector> <width> <height>                  resize a canvas node, in its own units
          zoom <selector> <%>                                 zoom a zoom box, a canvas or a fractal view
          pan <selector> <dx> <dy>                            drag what a canvas or a fractal view shows, in its units
          context-menu <selector>                             open its context menu as the menu key would
          connect <socket> <socket>                           join two sockets of nodes with a wire
          disconnect <socket> [<socket>]                      part a socket from one, or from all it is joined to
          dock <pane> top|left|bottom|right|fill|none [--beside <pane>]   dock a pane or a panel: to an edge, into the
                                                              documents, out into a window; beside a pane's panel
          window <selector> minimize | maximize | restore | close
          set <selector> <value>                              write a value: text, or a number
          type <text> [--into <selector>]                     type into the focused element, or into <selector>
          key <keys>... [--into <selector>]                   press keys: Alt, Ctrl+S, Shift+Tab, F, Enter... in the
                                                              focused window, or in <selector>'s (focusing it if it can)
          wait <selector> [--timeout 5s]                      wait until something matches
          wait <selector> <key>=<value>... [--timeout 5s]     wait until it matches, as expect checks it
          wait-idle                                           wait until the application has settled
          state                                               keyboard focus, windows, open popups
          shot [<selector>] [--out shot.png]                  a picture of it, or of the first window, to look at
          mark | errors [--since <mark>]                      the error journal: its newest entry, what came after a mark
          expect <selector> <key>=<value>...                  fail unless it matches: name, id, type, class, value, toggle,
                                                              selected, expanded, min, max, hscroll, vscroll, window, zoom,
                                                              dock, key,
                                                              left, top, width, height (on the screen, physical px),
                                                              enabled, offscreen, focus, or a property name
          absent <selector>                                   fail if anything matches
          run <scenario>                                      the commands of a file, one a line, up to the first failure
          run <folder>                                        every scenario in it, in name order, and a summary
          sweep [<tabs>] [--dwell 4s] [--passes <n>]          open every tab in turn (the gallery's by default): how long
                                                              each took to settle and what it left in the error journal

          --pipe <name>     the agent's pipe (default adam-auto)
          --allow-errors    an action may leave errors in the journal; by default that fails it

        A selector: id=Cut   name="Cut out"   type=Button,name=OK   class=RibbonButton
        A path: id=Shell/id=Cut (anywhere below)   id=List>type=ListItem (children only)
                type=ListItem[2] (the third match; [-1] the last)   id=Cut/next   id=Cut/previous   id=Cut/parent
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var arguments = new ScriptArguments(args);
        var pipe = arguments.Option("pipe") ?? DefaultPipe;
        try
        {
            switch (arguments.Command)
            {
                case "start":
                    return await StartAsync(arguments, pipe);
                case "stop":
                    return await StopAsync(pipe);
                case "run":
                    return await RunAsync(arguments, pipe);
                case "sweep":
                    return await SweepAsync(arguments, pipe);
                case { } command when ScriptInterpreter.Commands.Contains(command):
                    await using (var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout))
                    {
                        await session.RunCommandAsync(args, Console.Out);
                    }

                    return 0;
                default:
                    return Fail(Usage, 2);
            }
        }
        catch (AutomationException e)
        {
            return Fail(e.Message, 1);
        }
    }

    private static async Task<int> StartAsync(ScriptArguments arguments, string pipe)
    {
        if (await AnswersAsync(pipe))
        {
            return Fail($"An instance already answers on pipe '{pipe}'. Close it first: adam-auto stop", 1);
        }

        var options = new LaunchOptions { PipeName = pipe, StartTimeout = StartTimeout, CloseOnDispose = false };
        if (arguments.Option("theme") is { } theme)
        {
            options.Environment["ADAM_THEME"] = theme;
        }

        var exe = arguments.Option("exe") ?? Path.Combine(AppContext.BaseDirectory, DefaultApplication);
        if (!File.Exists(exe))
        {
            return Fail(arguments.Option("exe") == null
                ? "Name the application to start: adam-auto start --exe <path>"
                : $"No application at {Path.GetFullPath(exe)}.", 2);
        }

        await using var session = await AutomationSession.LaunchAsync(exe, options);
        if (arguments.Option("tab") is { } tab)
        {
            await session.Find(By.Id(GalleryTabs)).Find(By.Id(tab)).SelectAsync();
        }

        var windows = await session.WindowsAsync();
        Console.WriteLine($"Started {Path.GetFileName(exe)} (process {session.Process.Id}) on pipe '{pipe}'.");
        Console.WriteLine(ScriptPrinter.Line(windows[0]));
        return 0;
    }

    private static async Task<int> StopAsync(string pipe)
    {
        await using var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout);
        await session.SendAsync(new AutomationRequest { Command = AutomationCommand.Shutdown });
        Console.WriteLine("Closed.");
        return 0;
    }

    private static async Task<int> RunAsync(ScriptArguments arguments, string pipe)
    {
        var path = arguments.At(0);
        var files = path != null && Directory.Exists(path)
            ? Directory.GetFiles(path, "*.adam").OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList()
            : [path];

        await using var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout);
        var failed = new List<string>();
        foreach (var file in files)
        {
            if (files.Count > 1)
            {
                Console.WriteLine($"=== {Path.GetFileName(file)}");
            }

            try
            {
                await session.RunScenarioAsync(file, Console.Out);
            }
            catch (AutomationException e)
            {
                Console.Error.WriteLine(e.Message);
                failed.Add(Path.GetFileName(file));
            }
        }

        if (files.Count > 1)
        {
            Console.WriteLine(failed.Count == 0
                ? $"All {files.Count} scenarios passed."
                : $"{files.Count - failed.Count} of {files.Count} scenarios passed; failed: {string.Join(", ", failed)}.");
        }

        return failed.Count == 0 ? 0 : 1;
    }

    private static async Task<int> SweepAsync(ScriptArguments arguments, string pipe)
    {
        var tabs = arguments.At(0) ?? $"id={GalleryTabs}";
        var dwell = arguments.TimeOption("dwell") ?? TimeSpan.Zero;
        var passes = Math.Max(1, arguments.IntOption("passes"));
        await using var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout);
        var found = await session.SendAsync(new AutomationRequest { Command = AutomationCommand.Find, Target = $"{tabs}/type=TabItem" });
        if (!found.Ok)
        {
            return Fail(found.Error, 1);
        }

        var pages = found.Elements.Select(tab => tab.AutomationId is { Length: > 0 } id ? $"id={id}" : $"name=\"{tab.Name}\"").ToList();
        if (pages.Count == 0)
        {
            return Fail($"'{tabs}' holds no tabs.", 1);
        }

        var troubled = 0;
        for (var pass = 1; pass <= passes; pass++)
        {
            foreach (var page in pages)
            {
                var mark = (await session.SendAsync(new AutomationRequest { Command = AutomationCommand.Mark })).Mark;
                var watch = Stopwatch.StartNew();
                var selected = await session.SendAsync(new AutomationRequest
                {
                    Command = AutomationCommand.Select,
                    Target = $"{tabs}/{page}",
                    AllowErrors = true
                });
                await session.SendAsync(new AutomationRequest { Command = AutomationCommand.WaitIdle });
                var settled = watch.Elapsed;
                if (dwell > TimeSpan.Zero)
                {
                    await Task.Delay(dwell);
                }

                var errors = (await session.SendAsync(new AutomationRequest { Command = AutomationCommand.Errors, Since = mark })).Errors;
                var line = passes > 1 ? $"[{pass}] {page}" : page;
                if (!selected.Ok)
                {
                    troubled++;
                    Console.WriteLine($"{line}: {selected.Error}");
                    continue;
                }

                Console.WriteLine($"{line}: {settled.TotalMilliseconds:0} ms{(errors.Count == 0 ? string.Empty : $", {errors.Count} error(s)")}");
                foreach (var error in errors)
                {
                    Console.WriteLine($"  {ScriptPrinter.Error(error)}");
                }

                troubled += errors.Count > 0 ? 1 : 0;
            }
        }

        Console.WriteLine(troubled == 0
            ? $"Every page opened cleanly ({pages.Count} x {passes})."
            : $"{troubled} page visit(s) failed or left errors.");
        return troubled == 0 ? 0 : 1;
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
