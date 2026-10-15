using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.Resources;
using Adamantium.UI.Sandbox.Views;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>
/// The designer builds a tree for every full preview and lets the last one go. Every view the sandbox ships, shown and
/// then let go the way the designer does it, is collected - none is kept by the theme, the resources, the render marks
/// or the animation clock.
/// </summary>
[TestFixture]
public class SandboxViewsReleaseTests
{
    [Test]
    public void EverySandboxView_LetGoAsTheDesignerDoes_IsCollected()
    {
        var app = HeadlessApplication.Start<Fluent>();
        app.ThemeManager.AddStyleSet<RibbonShellStyleSet>();
        app.ResourceManager.AddSource(new ModuleResources(), typeof(ModuleIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(RibbonShellIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(CaptionIcons), ResourceScope.Global);
        _ = typeof(RibbonShellView).Assembly;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(dir.FullName, "Adamantium.UI.Sandbox", "Views")))
        {
            dir = dir.Parent;
        }

        var shown = new List<(string Name, WeakReference Root)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir.FullName, "Adamantium.UI.Sandbox"), "*.auml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .OrderBy(f => f))
        {
            if (ShowAndLetGo(File.ReadAllText(file)) is { } root)
            {
                shown.Add((Path.GetFileNameWithoutExtension(file), root));
            }
        }

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(shown.Count, Is.GreaterThan(50), "the sweep reached the views");
        Assert.That(shown.Where(view => view.Root.IsAlive).Select(view => view.Name), Is.Empty, "kept after being let go");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndLetGo(string text)
    {
        // A full preview starts its animation clock afresh, as the designer's does.
        AnimationManager.Reset();
        var load = AumlLoader.Load(text);
        if (load.Root is not IUIComponent root)
        {
            return null;
        }

        var window = root as Window ?? new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = root };
        try
        {
            window.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }
        catch (Exception)
        {
            // What a view needs from the whole application is not here; it was still built, styled and marked.
        }

        // The designer's frame consumes what changed; its next full preview lets the window go, and the frame of that
        // preview consumes what letting go marked.
        RenderDirty.Clear();
        window.Close();
        LoopSignal.Drain();
        DiscardedVisuals.ReleaseNow([window, .. load.SourceMap.Keys.OfType<IUIComponent>()]);
        RenderDirty.Clear();
        return new WeakReference(root);
    }
}
