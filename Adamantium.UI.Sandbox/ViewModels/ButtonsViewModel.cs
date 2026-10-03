using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Buttons &amp; toggles tab: a click counter driven by Button/RepeatButton commands, plus a small settings
/// panel where a ToggleSwitch/ToggleButton/CheckBox/RadioButton group drive live state - all through the view-model,
/// so the controls and what the page says about them stay in sync both ways. The words are the view's.</summary>
[ViewModel]
public partial class ButtonsViewModel : TabPageViewModel
{
    public ButtonsViewModel() : base("Buttons")
    {
        SelectedColor = Palette[0];   // seed so the templated header shows a real row from the start (no null-through-template)
    }

    // Click counter: a plain Button and an auto-repeating RepeatButton both invoke Add; the count shows live.
    [Bindable] private int _clickCount;

    [Command] private void Add() => ClickCount++;

    [Command] private void Reset() => ClickCount = 0;

    // ToggleSwitch: also gates the two buttons' IsEnabled (a cross-control interaction driven purely by the VM).
    [Bindable] private bool _actionsEnabled = true;

    // ToggleButton.
    [Bindable] private bool _notify = true;

    // Three-state CheckBox (bool? matches IsChecked exactly).
    [Bindable] private bool? _termsAccepted = false;

    // Mutually-exclusive RadioButton group (same GroupName in markup); the checked one is the plan.
    [Bindable] private bool _planFree = true;
    [Bindable] private bool _planPro;

    // DropDown (non-editable). A plain string list bound via ItemsSource; and an enum bound via EnumType (the view says
    // each member by its name, while SelectedPriority stays the real enum value).
    [Bindable] private string[] _cities = ["London", "Paris", "Berlin", "Tokyo", "New York", "Sydney"];
    [Bindable] private string _selectedCity;

    [Bindable] private Priority _selectedPriority = Priority.Normal;

    // Template-bound DropDown: items are ColorOption objects rendered by an ItemTemplate (swatch + name), not plain text.
    [Bindable] private ColorOption[] _palette =
    [
        new ColorOption("Ocean", "#0091F7"),
        new ColorOption("Forest", "#107C10"),
        new ColorOption("Grape", "#8764B8"),
        new ColorOption("Ember", "#CA5010"),
        new ColorOption("Rose", "#C42B72"),
    ];
    [Bindable] private ColorOption _selectedColor;
}
