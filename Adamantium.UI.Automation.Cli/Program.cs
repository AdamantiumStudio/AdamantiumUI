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
          start ... --virtual                                 on Linux, on a virtual monitor of its own instead of the
                                                              desktop: it takes no one's focus
          start ... --screen <id>                             its windows on the monitor whose id ends with <id>, as
                                                              DISPLAY2 on Windows; ADAM_AUTOMATION_SCREEN does it for all
          start ... --args <text>                             the application's own command line
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
          touch <selector> tap | hold | drag <x1,y1> <x2,y2>  a finger on it, inside the application; hold is a
                                                              long press, the touch way of a right click
          pen <selector> tap | hold | drag <x1,y1> <x2,y2> [--pressure <0..1>] [--barrel] [--eraser]
                                                              a pen on it; --barrel holds its side button;
                                                              --pressure <a..b> presses from a to b along a drag
          touches <selector>... tap | hold | drag <dx,dy>     a finger on each, up to ten, down and up together;
                                                              drag draws each by dx,dy of its own units
          pinch <selector> <scale> [--rotate <deg>] [--pan <dx,dy>]   two fingers drawn apart or together on it
          touch | touches | pinch | pen ... --os              the same through the system's own pointer input: it
                                                              presses whatever is on top of the desktop there, so
                                                              only while nobody uses the computer
          drop <selector> <target> [--before|--after]         carry it onto another element and let it go there
          scroll <selector>                                   bring a list's item into view, making its element
          scroll <selector> [--vertical <%>] [--horizontal <%>]   scroll a list or a scroll viewer to percents
          move <selector> <dx> <dy>                           move a splitter or a canvas node, in its own units
          resize <selector> <width> <height>                  resize a canvas node, in its own units
          zoom <selector> <%>                                 zoom a zoom box, a canvas or a fractal view
          pan <selector> <dx> <dy>                            drag what a canvas or a fractal view shows, in its units
          wheel <selector> <notches>                          turn the wheel over it, inside the application:
                                                              positive scrolls up, negative down
          context-menu <selector>                             open its context menu as the menu key would
          connect <socket> <socket>                           join two sockets of nodes with a wire
          disconnect <socket> [<socket>]                      part a socket from one, or from all it is joined to
          dock <pane> top|left|bottom|right|fill|none [--beside <pane>]   dock a pane or a panel: to an edge, into the
                                                              documents, out into a window; beside a pane's panel
          window <selector> minimize | maximize | restore | close | hide | show
          set <selector> <value>                              write a value: text, or a number
          caret <selector> <line>[:<col>] [--to <line>[:<col>]]
                                                              put the caret in a text, or select up to --to; lines
                                                              and columns from 1
          type <text> [--into <selector>]                     type into the focused element, or into <selector>
          key <keys>... [--into <selector>]                   press keys: Alt, Ctrl+S, Shift+Tab, F, Enter... in the
                                                              focused window, or in <selector>'s (focusing it if it can)
          wait <selector> [--timeout 5s]                      wait until something matches
          wait <selector> <key>=<value>... [--timeout 5s]     wait until it matches, as expect checks it
          wait-idle                                           wait until the application has settled
          state                                               keyboard focus, windows, open popups, the default
                                                              font, monitors
          shot [<selector>] [--out shot.png]                  a picture of it, or of the first window, to look at
          memory                                              collect all garbage, then the managed heap and private
                                                              memory left - one reading of a leak hunt
          theme [<name>] [--variant <name>]                   move onto a theme and variant once the swap settles;
                                                              alone, which one is in use and which there are;
                                                              --variant System follows the desktop's
          mark | errors [--since <mark>]                      the error journal: its newest entry, what came after a mark
          expect <selector> <key>=<value>...                  fail unless it matches: name, id, type, class, value, toggle,
                                                              selected, expanded, min, max, hscroll, vscroll, window, zoom,
                                                              dock, key,
                                                              left, top, width, height (on the screen, physical px),
                                                              enabled, offscreen, focus, or a property name
          absent <selector>                                   fail if anything matches
          sweep [<tabs>] [--dwell 4s] [--passes <n>]          open every tab in turn (the gallery's by default): how long
                [--themes] [--memory]                         each took to settle and what it left in the error journal;
                                                              after each pass, every theme in every variant and back,
                                                              and the memory left once all garbage is collected

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
                case "sweep":
                    return await SweepAsync(arguments, pipe);
                case { } command when Commands.Names.Contains(command):
                    await using (var session = await AutomationSession.AttachAsync(pipe, ConnectTimeout))
                    {
                        await Commands.RunAsync(session, arguments, Console.Out);
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

        var options = new LaunchOptions
        {
            PipeName = pipe,
            StartTimeout = StartTimeout,
            CloseOnDispose = false,
            VirtualDisplay = arguments.Flag("virtual"),
            Screen = arguments.Option("screen"),
            Arguments = arguments.Option("args")
        };
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

    private static async Task<int> SweepAsync(ScriptArguments arguments, string pipe)
    {
        var tabs = arguments.At(0) ?? $"id={GalleryTabs}";
        var dwell = arguments.TimeOption("dwell") ?? TimeSpan.Zero;
        var passes = Math.Max(1, arguments.IntOption("passes"));
        var themes = arguments.Flag("themes");
        var memory = arguments.Flag("memory");
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
        if (memory)
        {
            Console.WriteLine($"[0] memory: {await session.MemoryAsync()}");
        }

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

            if (themes)
            {
                troubled += await SweepThemesAsync(session, pass);
            }

            if (memory)
            {
                Console.WriteLine($"[{pass}] memory: {await session.MemoryAsync()}");
            }
        }

        Console.WriteLine(troubled == 0
            ? $"Every page opened cleanly ({pages.Count} x {passes})."
            : $"{troubled} page visit(s) or theme swap(s) failed or left errors.");
        return troubled == 0 ? 0 : 1;
    }

    private static async Task<int> SweepThemesAsync(AutomationSession session, int pass)
    {
        var listing = (await session.ThemeAsync()).Split('\n', StringSplitOptions.TrimEntries);
        var current = listing[0].Split(", ");
        var troubled = 0;
        foreach (var line in listing.Skip(1))
        {
            var colon = line.IndexOf(':');
            var name = line[..colon];
            foreach (var variant in line[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var mark = (await session.SendAsync(new AutomationRequest { Command = AutomationCommand.Mark })).Mark;
                var watch = Stopwatch.StartNew();
                try
                {
                    await session.ThemeAsync(name, variant);
                }
                catch (AutomationException e)
                {
                    troubled++;
                    Console.WriteLine($"[{pass}] theme {name} {variant}: {e.Message}");
                    continue;
                }

                var errors = (await session.SendAsync(new AutomationRequest { Command = AutomationCommand.Errors, Since = mark })).Errors;
                Console.WriteLine($"[{pass}] theme {name} {variant}: {watch.Elapsed.TotalMilliseconds:0} ms{(errors.Count == 0 ? string.Empty : $", {errors.Count} error(s)")}");
                foreach (var error in errors)
                {
                    Console.WriteLine($"  {ScriptPrinter.Error(error)}");
                }

                troubled += errors.Count > 0 ? 1 : 0;
            }
        }

        await session.ThemeAsync(current[0], current.Length > 1 ? current[1] : null);
        return troubled;
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
