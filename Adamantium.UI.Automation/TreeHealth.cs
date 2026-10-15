using System.Collections.Generic;
using System.Text;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;

namespace Adamantium.UI.Automation;

/// <summary>What a settled application must never show, checked after every step a driver takes: an element in a window
/// that has been thrown away - drawn as it was, its bindings closed - and content a presenter holds but does not show.
/// Both are what an optimization that lets go of elements too early looks like from outside; each is written to the
/// <see cref="ErrorJournal"/> once, so the step that caused it fails.</summary>
internal static class TreeHealth
{
    private static readonly object Sync = new();
    private static readonly HashSet<(System.Guid Node, string Problem)> Reported = [];

    /// <summary>Checks every window and its overlay; call on the loop thread once the application has settled.</summary>
    public static void Check(IReadOnlyList<IWindow> windows)
    {
        var found = new List<(System.Guid Node, string Problem)>();
        foreach (var window in windows)
        {
            Walk(window, found);
            if (window.PopupRoots == null)
            {
                continue;
            }

            foreach (var popup in window.PopupRoots)
            {
                Walk(popup, found);
            }
        }

        lock (Sync)
        {
            foreach (var problem in found)
            {
                if (Reported.Add(problem))
                {
                    ErrorJournal.Add("Tree", problem.Problem);
                }
            }
        }
    }

    private static void Walk(IUIComponent root, List<(System.Guid Node, string Problem)> found)
    {
        var stack = new Stack<IUIComponent>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (node is FundamentalUIComponent { Lifecycle: VisualLifecycle.Discarded or VisualLifecycle.Parked or VisualLifecycle.Recycled } dead)
            {
                found.Add((node.RenderId,
                    $"{PathOf(node)}: in a window, but {dead.Lifecycle}{(dead.ReleasedInBatch > 0 ? " and released" : string.Empty)}"));
            }

            if (node is ContentPresenter { Content: UIComponent content, IsMeasureValid: true, Visibility: Visibility.Visible } presenter
                && !ReferenceEquals(content.VisualParent, presenter))
            {
                found.Add((node.RenderId, $"{PathOf(node)}: holds {Name(content)} as its content, but does not show it " +
                                          $"(it is under {(content.VisualParent == null ? "nothing" : Name(content.VisualParent))})"));
            }

            foreach (var child in node.VisualChildren)
            {
                if (ReferenceEquals(child.VisualParent, node))
                {
                    stack.Push(child);
                }
            }
        }
    }

    private static string PathOf(IUIComponent node)
    {
        var parts = new List<string>();
        for (var link = node; link != null && parts.Count < 6; link = link.VisualParent)
        {
            parts.Add(Name(link));
        }

        parts.Reverse();
        return string.Join(" > ", parts);
    }

    private static string Name(IUIComponent node)
    {
        var text = new StringBuilder(node.GetType().Name);
        if (!string.IsNullOrEmpty(node.Name))
        {
            text.Append(" #").Append(node.Name);
        }

        return text.ToString();
    }
}
