using System;
using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A window's part in modality: what it keeps from input while it is modal, and what keeps it.</summary>
internal sealed class MacOSModality(MacOSPlatform platform, IWindow window, Action activate)
{
    private readonly List<MacOSModality> _blockedWindows = [];
    private readonly HashSet<MacOSModality> _blockers = [];
    private readonly HashSet<MacOSModality> _exempt = [];
    private bool _blocking;
    private volatile bool _isBlocked;
    private volatile bool _closed;

    /// <summary>Whether a modal window keeps this one from input.</summary>
    internal bool IsBlocked => _isBlocked;

    /// <summary>Applies <see cref="IWindow.IsModal"/> as it is now.</summary>
    internal void Update()
    {
        lock (platform.ModalLock)
        {
            var modal = window.IsModal && !_closed;
            if (modal == _blocking)
                return;

            _blocking = modal;
            if (modal)
            {
                _exempt.UnionWith(_blockers);
                platform.CollectOthers(this, _blockedWindows);
                foreach (var other in _blockedWindows)
                    other._blockers.Add(this);
            }
            else
            {
                foreach (var other in _blockedWindows)
                    other._blockers.Remove(this);
            }

            Recount();
            foreach (var other in _blockedWindows)
                other.Recount();

            if (!modal)
            {
                _blockedWindows.Clear();
                _exempt.Clear();
            }
        }
    }

    /// <summary>Activates what keeps this window from input - of modals opened over modals, the one nothing keeps from
    /// input itself.</summary>
    internal void ActivateBlocker()
    {
        MacOSModality modal = null;
        lock (platform.ModalLock)
        {
            foreach (var blocker in _blockers)
            {
                if (blocker._blocking && (modal == null || !blocker._isBlocked))
                    modal = blocker;
            }
        }

        modal?.Activate();
    }

    /// <summary>The window is closed: nothing keeps it any more.</summary>
    internal void Close()
    {
        _closed = true;
        lock (platform.ModalLock)
        {
            foreach (var blocker in _blockers)
                blocker._blockedWindows.Remove(this);
            _blockers.Clear();
        }
    }

    private void Activate() => activate();

    private void Recount()
    {
        var blocked = false;
        foreach (var blocker in _blockers)
        {
            if (!_blocking || !_exempt.Contains(blocker))
            {
                blocked = true;
                break;
            }
        }

        if (blocked == _isBlocked)
            return;

        _isBlocked = blocked;
        if (blocked)
            MacOSPlatform.DispatchInput(LetGo);
    }

    private void LetGo()
    {
        if (!_closed)
            BlockedWindowInput.LetGo(window);
    }
}
