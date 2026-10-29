using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.DBus;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Platforms.Linux.Accessibility;

internal sealed partial class AtSpiBridge
{
    private const char EventNameSeparator = ':';
    private const char EventWordSeparator = '-';
    private const char InterfaceSeparator = '.';

    private readonly object _windowsLock = new();
    private readonly HashSet<IWindow> _windows = [];
    private (int Id, string Content) _knownText = (-1, string.Empty);
    private WeakReference<AutomationPeer> _focused;

    /// <summary>How many times the registry's list of listeners has been taken in.</summary>
    internal int ListenerListsRead => Volatile.Read(ref _listenersApplied);

    private void OnListenersChanged(DBusMessage signal) => _ = RefreshListenersAsync();

    private async Task RefreshListenersAsync()
    {
        var refresh = Interlocked.Increment(ref _listenerRefreshes);
        try
        {
            ApplyListeners(refresh, await _bus.CallAsync(ListenersCall()).ConfigureAwait(false));
        }
        catch (Exception e) when (e is DBusException or IOException or ObjectDisposedException)
        {
            Serilog.Log.Logger.Debug(e, "The accessibility registry did not tell who listens");
        }
    }

    private DBusMessage ListenersCall() =>
        DBusMessage.MethodCall(_registry, Paths.Registry, Interfaces.Registry, Methods.GetRegisteredEvents);

    private void ApplyListeners(int refresh, DBusMessage reply)
    {
        var events = reply.Reader();
        var end = events.ReadArrayEnd(8);
        lock (_listenersLock)
        {
            if (refresh <= _listenersApplied)
            {
                return;
            }

            _listeners.Clear();
            while (events.Position < end)
            {
                events.Align(8);
                var listener = (events.ReadString(), events.ReadString());
                _listeners[listener] = _listeners.GetValueOrDefault(listener) + 1;
            }

            UpdateRelaying();
            Volatile.Write(ref _listenersApplied, refresh);
        }
    }

    private void UpdateRelaying()
    {
        var wanted = !_disposed && _listeners.Count > 0;
        if (wanted == _relaying)
        {
            return;
        }

        _relaying = wanted;
        if (wanted)
        {
            AutomationEvents.Raised += OnRaised;
            WindowBase.IsActiveProperty.Changed += OnActiveChanged;
            _post(() => Windows());
        }
        else
        {
            AutomationEvents.Raised -= OnRaised;
            WindowBase.IsActiveProperty.Changed -= OnActiveChanged;
        }
    }

    private bool Listening(string eventName)
    {
        lock (_listenersLock)
        {
            foreach (var (bus, listened) in _listeners.Keys)
            {
                if (Covers(listened, eventName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Covers(string listened, string eventName)
    {
        ReadOnlySpan<char> wanted = listened;
        ReadOnlySpan<char> given = eventName;
        while (true)
        {
            var wantedEnd = wanted.IndexOf(EventNameSeparator);
            var givenEnd = given.IndexOf(EventNameSeparator);
            var part = wantedEnd < 0 ? wanted : wanted[..wantedEnd];
            if (!part.IsEmpty && !SameName(part, givenEnd < 0 ? given : given[..givenEnd]))
            {
                return false;
            }

            if (wantedEnd < 0)
            {
                return true;
            }

            wanted = wanted[(wantedEnd + 1)..];
            given = givenEnd < 0 ? ReadOnlySpan<char>.Empty : given[(givenEnd + 1)..];
        }
    }

    private static bool SameName(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        int i = 0, j = 0;
        while (true)
        {
            while (i < first.Length && first[i] == EventWordSeparator)
            {
                i++;
            }

            while (j < second.Length && second[j] == EventWordSeparator)
            {
                j++;
            }

            if (i == first.Length || j == second.Length)
            {
                return i == first.Length && j == second.Length;
            }

            if (char.ToLowerInvariant(first[i++]) != char.ToLowerInvariant(second[j++]))
            {
                return false;
            }
        }
    }

    private void OnRaised(object sender, AutomationEventArgs e)
    {
        if (InSplash(e.Peer))
        {
            return;
        }

        var peer = Reachable(e.Peer);
        if (peer == null || (e.Event == AutomationEvent.PropertyChanged && !ReferenceEquals(peer, e.Peer)))
        {
            return;
        }

        switch (e.Event)
        {
            case AutomationEvent.FocusChanged:
                OnFocusChanged(peer);
                break;
            case AutomationEvent.PropertyChanged:
                OnPropertyChanged(peer, e);
                break;
            case AutomationEvent.ElementSelected:
                if ((peer.GetPattern(PatternId.SelectionItem) as ISelectionItemProvider)?.SelectionContainer is { } container &&
                    Reachable(container) is { } list)
                {
                    Emit(list, Interfaces.ObjectEvent, Signals.SelectionChanged, string.Empty, 0, 0, DBusVariant.Int32(0));
                }

                break;
            case AutomationEvent.TextSelectionChanged when TextOf(peer) is { } text:
                Emit(peer, Interfaces.ObjectEvent, Signals.TextSelectionChanged, string.Empty, 0, 0, DBusVariant.Int32(0));
                Emit(peer, Interfaces.ObjectEvent, Signals.TextCaretMoved, string.Empty, text.Caret, 0, DBusVariant.Int32(0));
                break;
            case AutomationEvent.TextChanged:
                EmitTextChange(peer);
                break;
        }
    }

    private static bool InSplash(AutomationPeer peer) => (peer as UIComponentAutomationPeer)?.Owner?.RootVisual is SplashWindow;

    private void OnFocusChanged(AutomationPeer peer)
    {
        if (_focused != null && _focused.TryGetTarget(out var previous) && !ReferenceEquals(previous, peer))
        {
            EmitState(previous, Details.Focused, false);
        }

        _focused = new WeakReference<AutomationPeer>(peer);
        if (TextOf(peer) is { } text)
        {
            _knownText = (peer.RuntimeId, text.Content);
        }

        Emit(peer, Interfaces.FocusEvent, Signals.Focus, string.Empty, 0, 0, DBusVariant.Int32(0));
        EmitState(peer, Details.Focused, true);
    }

    private void OnPropertyChanged(AutomationPeer peer, AutomationEventArgs e)
    {
        switch (e.Property)
        {
            case AutomationProperty.Name:
                Emit(peer, Interfaces.ObjectEvent, Signals.PropertyChange, Details.AccessibleName, 0, 0,
                    DBusVariant.String(peer.Name ?? string.Empty));
                break;
            case AutomationProperty.IsEnabled:
                EmitState(peer, Details.Enabled, Equals(e.NewValue, true));
                EmitState(peer, Details.Sensitive, Equals(e.NewValue, true));
                break;
            case AutomationProperty.ToggleState:
                EmitState(peer, Details.Checked, Equals(e.NewValue, ToggleState.On));
                EmitState(peer, Details.Indeterminate, Equals(e.NewValue, ToggleState.Indeterminate));
                break;
            case AutomationProperty.ExpandCollapseState:
                EmitState(peer, Details.Expanded, Equals(e.NewValue, ExpandCollapseState.Expanded));
                EmitState(peer, Details.Collapsed, Equals(e.NewValue, ExpandCollapseState.Collapsed));
                break;
            case AutomationProperty.IsSelected:
                EmitState(peer, Details.Selected, Equals(e.NewValue, true));
                break;
            case AutomationProperty.RangeValue when peer.GetPattern(PatternId.RangeValue) is IRangeValueProvider range:
                Emit(peer, Interfaces.ObjectEvent, Signals.PropertyChange, Details.AccessibleValue, 0, 0, DBusVariant.Double(range.Value));
                break;
            case AutomationProperty.Value:
                EmitTextChange(peer);
                break;
        }
    }

    private void OnActiveChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (sender is not IWindow window || window is SplashWindow || (window as UIComponent)?.GetAutomationPeer() is not { } peer)
        {
            return;
        }

        var active = Equals(e.NewValue, true);
        if (active && Track(window))
        {
            Emit(peer, Interfaces.WindowEvent, Signals.Create, string.Empty, 0, 0, DBusVariant.String(peer.Name ?? string.Empty));
            Emit(null, Interfaces.ObjectEvent, Signals.ChildrenChanged, Details.Add, IndexInParent(peer), 0, Reference(peer));
        }

        Emit(peer, Interfaces.WindowEvent, active ? Signals.Activate : Signals.Deactivate, string.Empty, 0, 0,
            DBusVariant.String(peer.Name ?? string.Empty));
        EmitState(peer, Details.Active, active);
    }

    private bool Track(IWindow window)
    {
        lock (_windowsLock)
        {
            if (_disposed || !_windows.Add(window))
            {
                return false;
            }

            window.Closed += OnWindowClosed;
            return true;
        }
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        var window = (IWindow)sender;
        window.Closed -= OnWindowClosed;
        lock (_windowsLock)
        {
            _windows.Remove(window);
        }

        if ((window as UIComponent)?.GetAutomationPeer() is { } peer)
        {
            Emit(peer, Interfaces.WindowEvent, Signals.Destroy, string.Empty, 0, 0, DBusVariant.String(peer.Name ?? string.Empty));
            Emit(null, Interfaces.ObjectEvent, Signals.ChildrenChanged, Details.Remove, -1, 0, Reference(peer));
        }
    }

    private void EmitTextChange(AutomationPeer peer)
    {
        if (TextOf(peer) is not { } text)
        {
            return;
        }

        var (id, was) = _knownText;
        var now = text.Content;
        if (id != peer.RuntimeId)
        {
            if (peer.HasKeyboardFocus)
            {
                _knownText = (peer.RuntimeId, now);
            }

            return;
        }

        _knownText = (id, now);
        if (was == now)
        {
            return;
        }

        var head = 0;
        while (head < was.Length && head < now.Length && was[head] == now[head])
        {
            head++;
        }

        while (head > 0 && (SplitsPair(was, head) || SplitsPair(now, head)))
        {
            head--;
        }

        var tail = 0;
        while (tail < was.Length - head && tail < now.Length - head && was[^(tail + 1)] == now[^(tail + 1)])
        {
            tail++;
        }

        while (tail > 0 && (SplitsPair(was, was.Length - tail) || SplitsPair(now, now.Length - tail)))
        {
            tail--;
        }

        var start = AtSpiText.Characters(was, head);
        var removed = was[head..(was.Length - tail)];
        var inserted = now[head..(now.Length - tail)];
        if (removed.Length > 0)
        {
            Emit(peer, Interfaces.ObjectEvent, Signals.TextChanged, Details.Delete, start,
                AtSpiText.Characters(removed, removed.Length), DBusVariant.String(removed));
        }

        if (inserted.Length > 0)
        {
            Emit(peer, Interfaces.ObjectEvent, Signals.TextChanged, Details.Insert, start,
                AtSpiText.Characters(inserted, inserted.Length), DBusVariant.String(inserted));
        }
    }

    private static bool SplitsPair(string text, int index) => index > 0 && index < text.Length && char.IsLowSurrogate(text[index]);

    private void EmitState(AutomationPeer peer, string state, bool on) =>
        Emit(peer, Interfaces.ObjectEvent, Signals.StateChanged, state, on ? 1 : 0, 0, DBusVariant.Int32(0));

    private void Emit(AutomationPeer peer, string interfaceName, string member, string detail, int detail1, int detail2,
        DBusVariant data)
    {
        if (!Listening(EventName(interfaceName, member, detail)))
        {
            return;
        }

        var signal = DBusMessage.Signal(PathOf(peer), interfaceName, member, Signatures.Event, writer =>
        {
            writer.WriteString(detail);
            writer.WriteInt32(detail1);
            writer.WriteInt32(detail2);
            writer.WriteVariant(data);
            writer.WriteArray(8, _ => { });
        });

        try
        {
            _bus.Send(signal);
        }
        catch (Exception e) when (e is DBusException or IOException or ObjectDisposedException)
        {
            Serilog.Log.Logger.Debug(e, "The accessibility bus is gone: {Member} was not told", member);
        }
    }

    private static string EventName(string interfaceName, string member, string detail)
    {
        var category = interfaceName[(interfaceName.LastIndexOf(InterfaceSeparator) + 1)..].ToLowerInvariant();
        if (string.Equals(category, member, StringComparison.OrdinalIgnoreCase))
        {
            return category + EventNameSeparator;
        }

        var name = new StringBuilder(category).Append(EventNameSeparator);
        for (var index = 0; index < member.Length; index++)
        {
            if (char.IsUpper(member[index]) && index > 0)
            {
                name.Append(EventWordSeparator);
            }

            name.Append(char.ToLowerInvariant(member[index]));
        }

        if (detail.Length > 0)
        {
            name.Append(EventNameSeparator).Append(detail);
        }

        return name.ToString();
    }
}
