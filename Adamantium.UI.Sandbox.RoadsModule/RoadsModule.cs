using Adamantium.MVVM;
using Adamantium.UI.Sandbox.Modules;
using Adamantium.UI.Sandbox.Roads.Localization;

namespace Adamantium.UI.Sandbox.Roads;

/// <summary>Roads over the terrain: laying them, bridging what they cross. A module the shell does not ship - installed
/// from this assembly, with its own commands for its own tabs (RoadsResources.auml).</summary>
public class RoadsModule : EditorModule
{
    private AdamantiumCommand _layRoad;
    private AdamantiumCommand _bridge;
    private AdamantiumCommand _crossing;

    public RoadsModule()
    {
        Phrases = RoadsStrings.Current;
        Name = nameof(RoadsStrings.Roads);
        Info = nameof(RoadsStrings.RoadsInfo);
        Description = nameof(RoadsStrings.RoadsDescription);
        Adds = nameof(RoadsStrings.RoadsAdds);
        Tabs = [nameof(RoadsStrings.Roads)];
        Requires = nameof(RoadsStrings.CoreOnly);
        Version = "1.0";
        Accent = "#B8732E";
        Section = ModuleSection.World;
        State = EditorModuleState.Installed;
    }

    public AdamantiumCommand LayRoadCommand => _layRoad ??= new AdamantiumCommand(() => Report?.Invoke("RoadLaid"));

    public AdamantiumCommand BridgeCommand => _bridge ??= new AdamantiumCommand(() => Report?.Invoke("BridgeBuilt"));

    public AdamantiumCommand CrossingCommand => _crossing ??= new AdamantiumCommand(() => Report?.Invoke("CrossingMade"));
}
