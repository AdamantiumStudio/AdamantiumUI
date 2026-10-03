using System;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>The ribbon tabs a command stands on - one command may stand on several. <see cref="All"/> is what the
/// customize page lists when it is asked for every command.</summary>
[Flags]
public enum ShellTab
{
    None = 0,
    Home = 1,
    Modeling = 2,
    Materials = 4,
    View = 8,
    Geometry = 16,
    Uv = 32,
    Light = 64,
    All = Home | Modeling | Materials | View | Geometry | Uv | Light
}
