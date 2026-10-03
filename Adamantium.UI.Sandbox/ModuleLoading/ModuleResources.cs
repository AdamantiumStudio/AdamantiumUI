using Adamantium.UI.Core;

namespace Adamantium.UI.Sandbox.ModuleLoading;

/// <summary>Owns the resources one module assembly brought - its tabs, by key.</summary>
public sealed class ModuleResources : AdamantiumComponent
{
    public string AssemblyName { get; init; }
}
