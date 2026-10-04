using System.Collections.Generic;
using Adamantium.UI.Sandbox.Modules;

namespace Adamantium.UI.Sandbox.ModuleLoading;

/// <summary>What a module assembly gave: its modules, ready for the catalog. Failed when the file is no assembly the shell
/// can load at all.</summary>
public sealed class LoadedModules
{
    public IReadOnlyList<EditorModule> Modules { get; init; } = [];

    public bool Failed { get; init; }
}
