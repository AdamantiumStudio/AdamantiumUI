using System.Collections.Generic;

namespace Adamantium.UI.Core;

/// <summary>Routes a render mark to the scope of the stage (content, overlay, adorners) drawing its component; unclaimed
/// subtrees use <see cref="Default"/>.</summary>
public static class RenderDirtyRouter
{
    /// <summary>The scope everything belongs to until a stage says otherwise - the window content.</summary>
    public static readonly RenderDirtyScope Default = new();

    // Every scope in existence, for the once-per-frame clear. Scopes are created by stages (a handful per window), never
    // per component, so this stays tiny.
    private static readonly List<RenderDirtyScope> Scopes = new() { Default };

    /// <summary>A scope for a surface that records from marks of its own - a window's content, a stage. Registered here
    /// so the app-wide events (a theme swap starting, the layout settling) reach it.</summary>
    public static RenderDirtyScope NewScope()
    {
        var scope = new RenderDirtyScope();
        lock (Scopes) Scopes.Add(scope);
        return scope;
    }

    /// <summary>...and drop it when its surface is gone. A window opened and closed all session would otherwise leave a
    /// scope behind for every one of them, and every app-wide event would walk them all.</summary>
    public static void Forget(RenderDirtyScope scope)
    {
        if (scope == null || ReferenceEquals(scope, Default)) return;
        lock (Scopes) Scopes.Remove(scope);
    }

    /// <summary>The scope this component's marks belong to - a field READ, because marking is the hottest path there is:
    /// a scrolling frame marks thousands of components, and asking each of them to walk up to its stage would put a tree
    /// walk into every one of those marks.</summary>
    public static RenderDirtyScope Of(IUIComponent component) => component?.RenderScope ?? Default;

    /// <summary>Every scope there is, for the once-per-frame clear.</summary>
    public static IReadOnlyList<RenderDirtyScope> All()
    {
        lock (Scopes) return Scopes.ToArray();
    }
}
