using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Automation;

/// <summary>Carries out automation requests inside the application, touching its trees only on the loop thread: the one
/// implementation behind the headless driver and the agent. Every action waits for the application to settle.</summary>
public sealed class AutomationExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WaitStep = TimeSpan.FromMilliseconds(20);

    private const int ClosestShown = 5;

    private readonly IAutomationHost _host;

    public AutomationExecutor(IAutomationHost host)
    {
        _host = host;
    }

    public async Task<AutomationReply> ExecuteAsync(AutomationRequest request)
    {
        var reply = await RunAsync(request);
        reply.Mark = ErrorJournal.Mark();
        return reply;
    }

    private async Task<AutomationReply> RunAsync(AutomationRequest request)
    {
        var timeout = request.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(request.TimeoutMs) : DefaultTimeout;
        try
        {
            switch (request.Command)
            {
                case AutomationCommand.WaitIdle:
                    await _host.WaitForIdleAsync(timeout);
                    return AutomationReply.Done();
                case AutomationCommand.WaitFor:
                    return await WaitForAsync(request.Target, timeout);
                case AutomationCommand.Shutdown:
                    await _host.ShutdownAsync();
                    return AutomationReply.Done();
                case AutomationCommand.Mark:
                    return AutomationReply.Done();
                case AutomationCommand.Errors:
                    return new AutomationReply { Ok = true, Errors = ErrorJournal.Since(request.Since) };
                case AutomationCommand.Shot:
                    return await ShotAsync(request, timeout);
                case AutomationCommand.Windows:
                case AutomationCommand.Tree:
                case AutomationCommand.Find:
                case AutomationCommand.Get:
                case AutomationCommand.Inspect:
                case AutomationCommand.Visual:
                case AutomationCommand.State:
                    return await _host.RunOnLoopAsync(() => Read(request), timeout);
                default:
                    return await ActAsync(request, timeout);
            }
        }
        catch (TimeoutException)
        {
            return AutomationReply.Failed(
                $"The application did not answer within {timeout.TotalSeconds:0.#} s; it may be frozen.");
        }
        catch (Exception e) when (e is AutomationException or FormatException)
        {
            return AutomationReply.Failed(e.Message);
        }
        catch (Exception e)
        {
            return AutomationReply.Failed($"{e.GetType().Name}: {e.Message}");
        }
    }

    private async Task<AutomationReply> ShotAsync(AutomationRequest request, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            throw new AutomationException("A picture needs a file to go to.");
        }

        var renderer = _host.Renderer ?? throw new AutomationException("Nothing is drawn here, so there is no picture to take.");
        var taken = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _host.RunOnLoopAsync(() =>
        {
            var element = string.IsNullOrWhiteSpace(request.Target)
                ? _host.Windows.OfType<UIComponent>().FirstOrDefault() ?? throw new AutomationException("No window is open.")
                : OwnerOf(Resolve(request.Target));
            renderer.RequestSnapshot(element, image => taken.TrySetResult(image));
            LoopSignal.Request();
            return true;
        }, timeout);

        using var picture = await taken.Task.WaitAsync(timeout);
        if (picture is not BitmapSource { PixelBytes: not null } bitmap)
        {
            throw new AutomationException("The element has no size to take a picture of.");
        }

        var path = Path.GetFullPath(request.Value);
        SnapshotFile.Save(bitmap, path);
        return new AutomationReply { Ok = true, Text = path };
    }

    private async Task<AutomationReply> ActAsync(AutomationRequest request, TimeSpan timeout)
    {
        var mark = ErrorJournal.Mark();
        await _host.RunOnLoopAsync(() => Act(request), timeout);
        await _host.WaitForIdleAsync(timeout);
        var reply = await _host.RunOnLoopAsync(() => Describe(request.Target), timeout);

        var quiet = ErrorJournal.Since(mark);
        if (quiet.Count == 0)
        {
            return reply;
        }

        reply.Errors = quiet;
        if (!request.AllowErrors)
        {
            reply.Ok = false;
            reply.Error = $"{request.Command} left {quiet.Count} error(s) behind:{Environment.NewLine}" +
                          string.Join(Environment.NewLine, quiet.Select(entry => $"  [{entry.Kind}] {entry.Message}"));
        }

        return reply;
    }

    private AutomationReply Read(AutomationRequest request) => request.Command switch
    {
        AutomationCommand.Windows => new AutomationReply { Ok = true, Elements = [.. Roots().Select(Info)] },
        AutomationCommand.Tree => new AutomationReply { Ok = true, Text = Tree(request.Target, request.Depth) },
        AutomationCommand.Find => new AutomationReply { Ok = true, Elements = [.. FindAll(request.Target).Select(Info)] },
        AutomationCommand.Inspect => Inspect(request),
        AutomationCommand.Visual => new AutomationReply
        {
            Ok = true,
            Text = ElementInspector.Visual(OwnerOf(Resolve(request.Target)), request.Depth)
        },
        AutomationCommand.State => new AutomationReply { Ok = true, Text = ElementInspector.State(_host.Windows) },
        _ => new AutomationReply { Ok = true, Elements = [Info(Resolve(request.Target))] }
    };

    private AutomationReply Inspect(AutomationRequest request)
    {
        var peer = Resolve(request.Target);
        return new AutomationReply { Ok = true, Details = ElementInspector.Inspect(OwnerOf(peer), Info(peer), request.Properties) };
    }

    private bool Act(AutomationRequest request)
    {
        if (request.Command == AutomationCommand.Type && string.IsNullOrEmpty(request.Target))
        {
            InputSimulator.Type(request.Value ?? string.Empty);
            return true;
        }

        var peer = Resolve(request.Target);
        if (!peer.IsEnabled)
        {
            throw new AutomationException($"{Label(peer)} is disabled.");
        }

        switch (request.Command)
        {
            case AutomationCommand.Invoke:
                Pattern<IInvokeProvider>(peer, PatternId.Invoke).Invoke();
                break;
            case AutomationCommand.Toggle:
                Pattern<IToggleProvider>(peer, PatternId.Toggle).Toggle();
                break;
            case AutomationCommand.SetValue:
                Pattern<IValueProvider>(peer, PatternId.Value).SetValue(request.Value ?? string.Empty);
                break;
            case AutomationCommand.Select:
                Pattern<ISelectionItemProvider>(peer, PatternId.SelectionItem).Select();
                break;
            case AutomationCommand.Click:
                InputSimulator.Click(OwnerOf(peer), Label(peer));
                break;
            case AutomationCommand.Type:
                peer.SetFocus();
                InputSimulator.Type(request.Value ?? string.Empty);
                break;
            default:
                throw new AutomationException($"{request.Command} is not an action.");
        }

        return true;
    }

    private async Task<AutomationReply> WaitForAsync(string target, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var found = await _host.RunOnLoopAsync(() => FindAll(target).Select(Info).FirstOrDefault(), timeout);
            if (found != null)
            {
                return new AutomationReply { Ok = true, Elements = [found] };
            }

            if (watch.Elapsed >= timeout)
            {
                return AutomationReply.Failed(
                    await _host.RunOnLoopAsync(() => NotFound(target, $"within {timeout.TotalSeconds:0.#} s"), timeout));
            }

            await _host.WaitForIdleAsync(timeout);
            await Task.Delay(WaitStep);
        }
    }

    private AutomationReply Describe(string target)
    {
        var peer = string.IsNullOrEmpty(target) ? null : FindAll(target).FirstOrDefault();
        return peer == null ? AutomationReply.Done() : new AutomationReply { Ok = true, Elements = [Info(peer)] };
    }

    private IEnumerable<AutomationPeer> Roots() =>
        _host.Windows.OfType<UIComponent>().Select(window => window.GetAutomationPeer()).Where(peer => peer != null);

    private List<AutomationPeer> FindAll(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new AutomationException("No element named: the command needs a selector.");
        }

        var path = By.ParsePath(target);
        var matches = Roots().SelectMany(SelfAndDescendants).Where(path[0].Matches).ToList();
        foreach (var step in path.Skip(1))
        {
            matches = matches.SelectMany(peer => SelfAndDescendants(peer).Skip(1)).Where(step.Matches).Distinct().ToList();
        }

        return matches;
    }

    private AutomationPeer Resolve(string target) =>
        FindAll(target).FirstOrDefault() ?? throw new AutomationException(NotFound(target, "now"));

    private string NotFound(string target, string when)
    {
        var words = By.ParsePath(target).Last().Values().Select(value => value.ToLowerInvariant()).ToList();
        var closest = Roots().SelectMany(SelfAndDescendants)
            .Where(peer => words.Any(word =>
                peer.AutomationId.ToLowerInvariant().Contains(word) || peer.Name.ToLowerInvariant().Contains(word)))
            .Take(ClosestShown)
            .Select(peer => $"  {PathOf(peer)}")
            .ToList();

        return closest.Count == 0
            ? $"Nothing matches '{target}' {when}."
            : $"Nothing matches '{target}' {when}. Closest:{Environment.NewLine}{string.Join(Environment.NewLine, closest)}";
    }

    private string Tree(string target, int depth)
    {
        var text = new StringBuilder();
        var tops = string.IsNullOrWhiteSpace(target) ? Roots() : FindAll(target).Take(1);
        foreach (var top in tops)
        {
            Write(top, 0);
        }

        return text.ToString();

        void Write(AutomationPeer peer, int level)
        {
            text.Append(' ', level * 2).Append(Label(peer));
            if (peer.IsOffscreen)
            {
                text.Append(" (offscreen)");
            }

            if (!peer.IsEnabled)
            {
                text.Append(" (disabled)");
            }

            text.AppendLine();
            if (depth > 0 && level + 1 >= depth)
            {
                return;
            }

            foreach (var child in peer.GetChildren())
            {
                Write(child, level + 1);
            }
        }
    }

    private static IEnumerable<AutomationPeer> SelfAndDescendants(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren())
        {
            foreach (var below in SelfAndDescendants(child))
            {
                yield return below;
            }
        }
    }

    private static T Pattern<T>(AutomationPeer peer, PatternId pattern) where T : class =>
        peer.GetPattern(pattern) as T ?? throw new AutomationException($"{Label(peer)} cannot {pattern}.");

    private static UIComponent OwnerOf(AutomationPeer peer) =>
        (peer as UIComponentAutomationPeer)?.Owner ?? throw new AutomationException($"{Label(peer)} stands for no element of the tree.");

    private static ElementInfo Info(AutomationPeer peer)
    {
        var bounds = peer.BoundingRectangle;
        var info = new ElementInfo
        {
            RuntimeId = peer.RuntimeId,
            ControlType = peer.ControlType.ToString(),
            Name = peer.Name,
            AutomationId = peer.AutomationId,
            ClassName = peer.ClassName,
            Path = PathOf(peer),
            IsEnabled = peer.IsEnabled,
            IsOffscreen = peer.IsOffscreen,
            HasKeyboardFocus = peer.HasKeyboardFocus,
            Bounds = [bounds.X, bounds.Y, bounds.Width, bounds.Height],
            Patterns = [.. Enum.GetValues<PatternId>().Where(pattern => peer.GetPattern(pattern) != null).Select(pattern => pattern.ToString())]
        };

        if (peer.GetPattern(PatternId.Value) is IValueProvider value)
        {
            info.Value = value.Value;
        }

        if (peer.GetPattern(PatternId.Toggle) is IToggleProvider toggle)
        {
            info.ToggleState = toggle.ToggleState.ToString();
        }

        if (peer.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider item)
        {
            info.IsSelected = item.IsSelected;
        }

        return info;
    }

    private static string PathOf(AutomationPeer peer)
    {
        var labels = new List<string>();
        for (var at = peer; at != null; at = at.GetParent())
        {
            labels.Add(Label(at));
        }

        labels.Reverse();
        return string.Join(" > ", labels);
    }

    private static string Label(AutomationPeer peer)
    {
        var label = new StringBuilder(peer.ControlType.ToString());
        if (peer.Name.Length > 0)
        {
            label.Append(" \"").Append(peer.Name).Append('"');
        }

        if (peer.AutomationId.Length > 0)
        {
            label.Append(" #").Append(peer.AutomationId);
        }

        return label.Append(" [").Append(peer.ClassName).Append(']').ToString();
    }
}
