using Adamantium.Core.Commands;
using Adamantium.UI.Controls;
using Adamantium.UI.Sandbox.Modules;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>One row of the "Modules" drop-down, as data: a module of the document - a switch for its tabs - or a command,
/// or a divider. The menu keeps one row per module, so a row stays put while its module changes.</summary>
public class ModulesMenuRow : ISeparatorItem
{
    public EditorModule Module { get; init; }

    /// <summary>A command row's label, as the key of its phrase.</summary>
    public string Title { get; init; }

    public ICommand Command { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsModule => Module != null;

    public bool IsCommand => Module == null;
}
