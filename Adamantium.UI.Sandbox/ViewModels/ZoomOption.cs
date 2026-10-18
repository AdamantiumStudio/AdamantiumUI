using Adamantium.Core.Commands;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>One segment of the view-model-made segmented control: its label and the command it runs when chosen.</summary>
public sealed class ZoomOption
{
    public ZoomOption(string name, ICommand apply)
    {
        Name = name;
        Apply = apply;
    }

    public string Name { get; }

    public ICommand Apply { get; }
}
