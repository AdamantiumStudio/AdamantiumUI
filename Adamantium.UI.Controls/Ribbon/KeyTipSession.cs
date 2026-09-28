using System.Collections.Generic;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>One run of key-tip mode: a stack of levels, typed keys and badges; Escape steps back one level. Driven only by
/// <see cref="Press"/>, so it is testable without a window.</summary>
public class KeyTipSession
{
    private readonly AdornerLayer _layer;
    private readonly List<IUIComponent> _scopes = [];
    private readonly List<KeyTipAdorner> _badges = [];
    private IReadOnlyList<IUIComponent> _candidates = [];
    private string _typed = string.Empty;

    public KeyTipSession(IUIComponent root, AdornerLayer layer = null) : this([root], layer)
    {
    }

    /// <summary>The first level is gathered from SEVERAL places at once, because Office's is: the tab strip and the
    /// application menu are the band's, while the quick-access bar sits in the caption. Walking their common ancestor
    /// instead would badge the whole window, the open tab's commands included - and those belong one level down.</summary>
    public KeyTipSession(IReadOnlyList<IUIComponent> roots, AdornerLayer layer = null)
    {
        _roots = roots ?? [];
        _layer = layer;
    }

    private readonly IReadOnlyList<IUIComponent> _roots;

    /// <summary>The level everything starts at.</summary>
    public IUIComponent Root => _roots.Count > 0 ? _roots[0] : null;

    public bool IsActive { get; private set; }

    /// <summary>The level currently showing badges: the top level, or whatever was descended into.</summary>
    public IUIComponent Scope => _scopes.Count > 0 ? _scopes[^1] : Root;

    /// <summary>Where the current level's badges come from. The top level has its several roots; a deeper one is a
    /// single scope - and a scope may point ELSEWHERE for its contents (a tab header's commands are shown by the band,
    /// not underneath the strip).</summary>
    private IReadOnlyList<IUIComponent> Contents =>
        _scopes.Count <= 1 ? _roots : [(Scope as IKeyTipScope)?.KeyTipContent ?? Scope];

    /// <summary>What is still reachable after what has been typed - the badges on screen right now.</summary>
    public IReadOnlyList<IUIComponent> Candidates => _candidates;

    public void Begin()
    {
        if (IsActive) return;

        IsActive = true;
        _scopes.Clear();
        _scopes.Add(Root);
        Enter();
    }

    public void End()
    {
        if (!IsActive) return;

        IsActive = false;
        _scopes.Clear();
        _candidates = [];
        _typed = string.Empty;
        _pending = false;
        ClearBadges();
    }

    /// <summary>One keystroke: the typed character, then <paramref name="alternate"/>, the same key's Latin letter, so
    /// Latin tips work on other layouts. Returns whether it was consumed.</summary>
    public bool Press(char key, char? alternate = null)
    {
        if (!IsActive) return false;

        if (Match(key)) return true;
        if (alternate is { } other && !char.ToUpperInvariant(other).Equals(char.ToUpperInvariant(key)) && Match(other))
            return true;

        // Nothing answers to it either way. Office leaves the mode rather than sitting in a state no key can escape.
        End();
        return true;
    }

    private bool Match(char key)
    {
        var typed = _typed + char.ToUpperInvariant(key);
        var narrowed = KeyTipService.Narrow(Offered(), typed);

        // Nothing answers to THIS reading of the keystroke - the caller may still have another to try.
        if (narrowed.Count == 0) return false;

        // Still ambiguous (an "F" that could become "FN"): keep the letters and show only what survives.
        if (narrowed.Count > 1 || !string.Equals(KeyTipService.GetKeyTip(narrowed[0]), typed,
                System.StringComparison.OrdinalIgnoreCase))
        {
            _typed = typed;
            _candidates = narrowed;
            ShowBadges();
            return true;
        }

        Activate(narrowed[0]);
        return true;
    }

    /// <summary>Back out one level; from the top level, out of the mode. Typed letters go first - Escape after a half-
    /// typed key tip means "not that one", not "leave".</summary>
    public void Escape()
    {
        if (!IsActive) return;

        if (_typed.Length > 0)
        {
            _typed = string.Empty;
            Enter();
            return;
        }

        if (_scopes.Count <= 1)
        {
            End();
            return;
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        Enter();
    }

    private void Activate(IUIComponent target)
    {
        // A LEVEL: descend into it rather than run it. The tab still has to be told, so it can select itself - the
        // badges of the next level come from what it then shows.
        if (KeyTipService.GetIsScope(target))
        {
            // Descending into the level one is ALREADY on changes nothing, so there is nothing to wait for. Asked
            // before the press, because the press is what would make it true.
            var alreadyThere = (target as ISelectable)?.IsSelected == true;

            (target as IKeyTipTarget)?.PressKeyTip();
            _scopes.Add(target);
            _typed = string.Empty;

            if (alreadyThere)
            {
                Enter();
                return;
            }

            _candidates = [];
            ClearBadges();
            // Not shown yet: the band still holds the old tab; the owner calls Refresh once layout has settled.
            _pending = true;
            return;
        }

        if (target is IKeyTipTarget custom) custom.PressKeyTip();
        else (target as ButtonBase)?.PerformClick();

        End();
    }

    private void Enter()
    {
        _typed = string.Empty;
        _candidates = Offered();
        ShowBadges();
    }

    /// <summary>Who this level offers, each with a key tip - the ones the author did not name get theirs here. Named
    /// across the WHOLE level at once, so letters cannot collide between the tab strip and the caption's bar.</summary>
    private IReadOnlyList<IUIComponent> Offered()
    {
        var participants = new List<IUIComponent>();
        foreach (var root in Contents)
        {
            participants.AddRange(KeyTipService.Candidates(root));
        }

        KeyTipService.AutoAssign(participants);
        return participants;
    }

    /// <summary>Re-read the level once layout has settled. A tab's commands are not in the tree at the moment its
    /// header is pressed - the band shows them on the pass that follows - so the level a descent leads to is read HERE,
    /// not there.</summary>
    public void Refresh()
    {
        if (!IsActive || _typed.Length > 0) return;

        var offered = Offered();
        if (!_pending && offered.Count == _candidates.Count) return;

        _pending = false;
        _candidates = offered;
        ShowBadges();
    }

    private bool _pending;

    private void ShowBadges()
    {
        ClearBadges();
        if (_layer == null) return;

        foreach (var candidate in _candidates)
        {
            var badge = new KeyTipAdorner(candidate, KeyTipService.GetKeyTip(candidate),
                KeyTipService.GetPlacement(candidate));
            _badges.Add(badge);
            _layer.Add(badge);
        }
    }

    private void ClearBadges()
    {
        foreach (var badge in _badges) _layer?.Remove(badge);
        _badges.Clear();
    }
}
