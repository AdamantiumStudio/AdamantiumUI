namespace Adamantium.UI.Sandbox.Modules;

/// <summary>A tab a module brings into the ribbon: the module, and the key of the tab's name in its table.</summary>
public sealed class ModuleTab
{
    public EditorModule Module { get; init; }

    public string Name { get; init; }
}
