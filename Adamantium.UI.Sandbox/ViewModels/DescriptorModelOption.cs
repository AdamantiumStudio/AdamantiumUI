using Adamantium.Graphics.Core;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>One descriptor model the sandbox can start with: offered always, choosable only where this GPU has it.</summary>
public sealed record DescriptorModelOption(DescriptorModel Model, bool IsSupported)
{
    /// <summary>Whether the row shows why it cannot be chosen.</summary>
    public bool IsUnsupported => !IsSupported;

    public override string ToString() => Model.ToString();
}
