namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>One row of the ItemsControl demo (see ItemsDemoView.auml / ItemsDemoViewModel).</summary>
public class DemoItem
{
    public string Name { get; init; }

    /// <summary>The name: what a row without an element yet is called to automation, as in WPF.</summary>
    public override string ToString() => Name;
}
