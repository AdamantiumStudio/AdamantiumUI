using System;
using Adamantium.MVVM;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Sandbox.Modules;

/// <summary>A module the editor knows of - one theme a document can be made of: the tabs it brings and where it stands
/// with this document. Its words are phrase keys of its own table, <see cref="Phrases"/>. A module loaded from a file is a
/// class derived from this one: its own commands are what its tabs run.</summary>
[ViewModel]
public partial class EditorModule
{
    [Bindable] private EditorModuleState _state;

    /// <summary>Whether its tabs show. A hidden module still works - only its tabs are put away.</summary>
    [Bindable] private bool _isShown = true;

    /// <summary>The warning before it is taken out of the document is up.</summary>
    [Bindable] private bool _isDetachAsked;

    /// <summary>The warning before its data is deleted from the document is up.</summary>
    [Bindable] private bool _isDeleteAsked;

    private ImageSource _icon;

    /// <summary>The table its words are keys of: the shell's for a module the shell ships, the module's own for one
    /// loaded from a file. A word the table lacks is said as it is.</summary>
    public LocalizedStrings Phrases { get; init; }

    public string Name { get; init; }

    public string Info { get; init; }

    /// <summary>What it brings into the ribbon.</summary>
    public string Adds { get; init; }

    /// <summary>What this document holds of it; null for a module the document never had.</summary>
    public string Content { get; init; }

    /// <summary>What in this document leans on it, and is marked when it goes.</summary>
    public string Dependents { get; init; }

    public string Version { get; init; }

    /// <summary>Its color, as text the binding parses into a brush.</summary>
    public string Accent { get; init; }

    /// <summary>The key of its icon among the application's resources: its name and "Icon".</summary>
    public string IconKey => Name + "Icon";

    /// <summary>Its icon - a <see cref="DrawingImage"/> the module states among the application's resources, found by
    /// <see cref="IconKey"/> when first asked.</summary>
    public ImageSource Icon => _icon ??= UIAppContext.Current.ResourceManager.FindResource(IconKey) as ImageSource;

    public ModuleSection Section { get; init; }

    /// <summary>The key of the piece of view that is this module's tabs: its name and "Ribbon".</summary>
    public string RibbonKey => Name + "Ribbon";

    /// <summary>Where the module says what its commands did - the name of what was done, for the shell's status line.</summary>
    public Action<string> Report { get; set; }

    public bool IsInDocument => State == EditorModuleState.InDocument;

    /// <summary>What its tabs hang on: in the document and not hidden.</summary>
    public bool HasTabs => IsInDocument && IsShown;

    partial void OnStateChanged(EditorModuleState value)
    {
        RaisePropertyChanged(nameof(IsInDocument));
        RaisePropertyChanged(nameof(HasTabs));
    }

    partial void OnIsShownChanged(bool value) => RaisePropertyChanged(nameof(HasTabs));

    /// <summary>Takes both warnings down - the place that asked has closed.</summary>
    public void ForgetQuestions()
    {
        IsDetachAsked = false;
        IsDeleteAsked = false;
    }

    [Command] private void AskDetach() => IsDetachAsked = true;

    [Command] private void Detach()
    {
        IsDetachAsked = false;
        State = EditorModuleState.Detached;
    }

    [Command] private void KeepAttached() => IsDetachAsked = false;

    [Command] private void Attach()
    {
        IsShown = true;
        State = EditorModuleState.InDocument;
    }

    [Command] private void AskDeleteData() => IsDeleteAsked = true;

    [Command] private void DeleteData()
    {
        IsDeleteAsked = false;
        State = EditorModuleState.Installed;
    }

    [Command] private void KeepData() => IsDeleteAsked = false;

    [Command] private void Install() => State = EditorModuleState.Installed;

    [Command] private void Update() => State = EditorModuleState.Installed;

    [Command] private void Uninstall() => State = EditorModuleState.Available;

    [Command] private void Use(object action) => Report?.Invoke(action as string);
}
