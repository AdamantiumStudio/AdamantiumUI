using System.Collections.Generic;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Every command of the shell, each under its own name for the ribbon to draw its button from, and all of them in
/// <see cref="All"/> for the customize page.</summary>
public sealed class ShellCommands
{
    public ShellCommands(RibbonShellViewModel shell)
    {
        Save = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Save),
            IconData = "M3,2 L11,2 L13,4 L13,13 L3,13 Z M5,2 L5,6 L11,6 L11,2",
            Command = shell.SaveCommand
        };
        Undo = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Undo),
            IconData = "M6,4 L2,7 L6,10 M2,7 L10,7 A3,3 0 0 1 10,13 L8,13",
            Command = shell.UndoCommand
        };
        Redo = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Redo),
            IconData = "M8,4 L12,7 L8,10 M12,7 L4,7 A3,3 0 0 0 4,13 L6,13",
            Command = shell.RedoCommand
        };

        Paste = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Paste),
            Tabs = ShellTab.Home,
            IconData = "M4,2 L12,2 L12,14 L4,14 Z M6,2 L6,4 L10,4 L10,2",
            Command = shell.PasteCommand,
            DropDownItems = shell.PasteOptions
        };
        Cut = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Cut),
            Tabs = ShellTab.Home,
            IconData = "M3,2 L13,12 M13,2 L3,12",
            Command = shell.CutCommand
        };
        Copy = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Copy),
            Tabs = ShellTab.Home,
            IconData = "M3,3 L10,3 L10,11 L3,11 Z M6,6 L13,6 L13,14 L6,14 Z",
            Command = shell.CopyCommand
        };
        Duplicate = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Duplicate),
            Tabs = ShellTab.Home,
            IconData = "M2,2 L10,2 L10,10 L2,10 Z M6,6 L14,6 L14,14 L6,14 Z",
            Command = shell.DuplicateCommand
        };
        Entity = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Entity),
            Tabs = ShellTab.Home,
            IconData = "M8,2 L8,14 M2,8 L14,8",
            Command = shell.AddEntityCommand
        };
        Delete = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Delete),
            Tabs = ShellTab.Home | ShellTab.View,
            IconData = "M3,4 L13,4 M5,4 L5,14 L11,14 L11,4 M6,2 L10,2",
            Command = shell.DeleteCommand
        };
        SelectAll = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SelectAll),
            Tabs = ShellTab.Home | ShellTab.View,
            IconData = "M2,2 L14,2 L14,14 L2,14 Z M5,5 L11,5 L11,11 L5,11 Z",
            Command = shell.SelectAllCommand
        };
        SelectNone = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SelectNone),
            Tabs = ShellTab.Home | ShellTab.View,
            IconData = "M2,2 L14,2 L14,14 L2,14 Z",
            Command = shell.SelectNoneCommand
        };
        Wireframe = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Wireframe),
            Tabs = ShellTab.Home | ShellTab.Materials | ShellTab.View,
            IconData = "M8,2 L14,5 L14,11 L8,14 L2,11 L2,5 Z M2,5 L8,8 L14,5 M8,8 L8,14",
            IsChecked = false
        };
        Grid = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Grid),
            Tabs = ShellTab.Home | ShellTab.View,
            IconData = "M2,2 L14,2 L14,14 L2,14 Z M6,2 L6,14 M10,2 L10,14 M2,6 L14,6 M2,10 L14,10",
            IsChecked = true
        };
        Gizmos = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Gizmos),
            Tabs = ShellTab.Home | ShellTab.View,
            IconData = "M8,14 L8,4 M8,4 L5,7 M8,4 L11,7 M2,14 L14,14",
            IsChecked = true
        };
        MoveQuickAccess = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.MoveQuickAccess),
            Tabs = ShellTab.Home,
            IconData = "M3,4 L13,4 M3,8 L13,8 M6,11 L8,13 L10,11",
            Command = shell.MoveQuickAccessCommand
        };
        AlignLeft = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.AlignLeft),
            Tabs = ShellTab.Home,
            IconData = "M2,2 L2,14 M4,5 L12,5 M4,11 L9,11",
            Command = shell.AlignLeftCommand
        };
        Center = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Center),
            Tabs = ShellTab.Home,
            IconData = "M8,2 L8,14 M4,5 L12,5 M6,11 L10,11",
            Command = shell.AlignCenterCommand
        };
        Distribute = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Distribute),
            Tabs = ShellTab.Home,
            IconData = "M2,2 L2,14 M14,2 L14,14 M8,5 L8,11",
            Command = shell.DistributeCommand
        };
        Group = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Group),
            Tabs = ShellTab.Home,
            IconData = "M3,3 L9,3 L9,9 L3,9 Z M7,7 L13,7 L13,13 L7,13 Z",
            Command = shell.GroupSelectionCommand
        };
        Forward = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Forward),
            Tabs = ShellTab.Home,
            IconData = "M8,3 L8,13 M5,6 L8,3 L11,6",
            Command = shell.BringForwardCommand
        };
        Backward = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Backward),
            Tabs = ShellTab.Home,
            IconData = "M8,3 L8,13 M5,10 L8,13 L11,10",
            Command = shell.SendBackwardCommand
        };
        AddLight = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.AddLight),
            Tabs = ShellTab.Home | ShellTab.Light,
            IconData = "M8,2 L8,4 M8,12 L8,14 M2,8 L4,8 M12,8 L14,8 M8,5 A3,3 0 1 0 8,11 A3,3 0 1 0 8,5",
            Command = shell.AddLightCommand
        };
        Bake = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Bake),
            Tabs = ShellTab.Home | ShellTab.Light,
            IconData = "M3,12 L13,12 M5,12 L5,7 M8,12 L8,4 M11,12 L11,9",
            Command = shell.BakeLightingCommand
        };
        Collider = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Collider),
            Tabs = ShellTab.Home,
            IconData = "M3,3 L13,3 L13,13 L3,13 Z M3,3 L13,13 M13,3 L3,13",
            Command = shell.AddColliderCommand
        };
        Simulate = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Simulate),
            Tabs = ShellTab.Home,
            IconData = "M5,3 L13,8 L5,13 Z",
            Command = shell.SimulateCommand
        };
        Measure = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Measure),
            Tabs = ShellTab.Home | ShellTab.Uv,
            IconData = "M2,6 L14,6 L14,10 L2,10 Z M5,6 L5,8 M8,6 L8,8 M11,6 L11,8",
            Command = shell.MeasureCommand
        };
        Annotate = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Annotate),
            Tabs = ShellTab.Home,
            IconData = "M3,3 L11,3 L11,11 L7,11 L4,14 L4,11 L3,11 Z",
            Command = shell.AnnotateCommand
        };

        Add = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Add),
            Tabs = ShellTab.Modeling,
            IconData = "M8,2 L8,14 M2,8 L14,8",
            DropDownItems = shell.PrimitiveOptions
        };
        Cube = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Cube),
            Tabs = ShellTab.Modeling,
            IconData = "M8,2 L14,5 L14,11 L8,14 L2,11 L2,5 Z M2,5 L8,8 L14,5 M8,8 L8,14",
            Command = shell.AddCubeCommand
        };
        Sphere = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Sphere),
            Tabs = ShellTab.Modeling,
            IconData = "M8,2 A6,6 0 1 0 8,14 A6,6 0 1 0 8,2 M2,8 L14,8",
            Command = shell.AddSphereCommand
        };
        Plane = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Plane),
            Tabs = ShellTab.Modeling,
            IconData = "M2,10 L8,6 L14,10 L8,14 Z",
            Command = shell.AddPlaneCommand
        };
        Extrude = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Extrude),
            Tabs = ShellTab.Modeling | ShellTab.Geometry,
            IconData = "M2,9 L8,5 L14,9 L8,13 Z M8,5 L8,1",
            Command = shell.ExtrudeCommand
        };
        Bevel = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Bevel),
            Tabs = ShellTab.Modeling | ShellTab.Geometry,
            IconData = "M3,13 L3,6 L6,3 L13,3 M3,6 L13,6 L13,3 M13,6 L13,13 L3,13",
            Command = shell.BevelCommand
        };
        Subdivide = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Subdivide),
            Tabs = ShellTab.Modeling | ShellTab.Geometry,
            IconData = "M2,2 L14,2 L14,14 L2,14 Z M8,2 L8,14 M2,8 L14,8",
            Command = shell.SubdivideCommand
        };
        Snap = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Snap),
            Tabs = ShellTab.Modeling,
            IconData = "M8,2 L8,14 M2,8 L14,8 M5,5 L11,11 M11,5 L5,11",
            IsChecked = false,
            MenuRows = shell.SnapMenuRows
        };
        SnapToVertices = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SnapToVertices),
            Tabs = ShellTab.Modeling,
            IconData = "M3,3 L5,3 L5,5 L3,5 Z M11,3 L13,3 L13,5 L11,5 Z M3,11 L5,11 L5,13 L3,13 Z M11,11 L13,11 L13,13 L11,13 Z",
            IsChecked = false,
            MenuRows = shell.SnapMenuRows
        };
        SnapToEdges = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SnapToEdges),
            Tabs = ShellTab.Modeling,
            IconData = "M3,3 L3,13 L13,13 M3,3 L13,13",
            IsChecked = false,
            MenuRows = shell.SnapMenuRows
        };
        SnapToFaces = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SnapToFaces),
            Tabs = ShellTab.Modeling,
            IconData = "M3,5 L13,5 L13,13 L3,13 Z M3,5 L8,9 L13,5",
            IsChecked = false,
            MenuRows = shell.SnapMenuRows
        };

        NewMaterial = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.NewMaterial),
            Tabs = ShellTab.Materials,
            IconData = "M3,2 L11,2 L13,4 L13,14 L3,14 Z M8,6 L8,11 M5.5,8.5 L10.5,8.5",
            Command = shell.NewMaterialCommand
        };
        Albedo = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Albedo),
            Tabs = ShellTab.Materials,
            IconData = "M8,2 A6,6 0 1 0 8,14 A6,6 0 1 0 8,2",
            Command = shell.EditAlbedoCommand
        };
        Normal = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Normal),
            Tabs = ShellTab.Materials,
            IconData = "M2,12 L8,4 L14,12 Z",
            Command = shell.EditNormalCommand
        };
        Roughness = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Roughness),
            Tabs = ShellTab.Materials,
            IconData = "M2,11 L5,7 L8,11 L11,5 L14,11",
            Command = shell.EditRoughnessCommand
        };

        Unwrap = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Unwrap),
            Tabs = ShellTab.Uv,
            IconData = "M2,2 L14,2 L14,14 L2,14 Z M5,2 L5,14 M11,2 L11,14",
            Command = shell.UnwrapCommand
        };

        All =
        [
            Save, Undo, Redo,
            Paste, Cut, Copy, Duplicate, Entity, Delete, SelectAll, SelectNone, Wireframe, Grid, Gizmos, MoveQuickAccess,
            AlignLeft, Center, Distribute, Group, Forward, Backward, AddLight, Bake, Collider, Simulate, Measure, Annotate,
            Add, Cube, Sphere, Plane, Extrude, Bevel, Subdivide, Snap, SnapToVertices, SnapToEdges, SnapToFaces,
            NewMaterial, Albedo, Normal, Roughness,
            Unwrap
        ];
    }

    public ShellCommand Save { get; }

    public ShellCommand Undo { get; }

    public ShellCommand Redo { get; }

    public ShellCommand Paste { get; }

    public ShellCommand Cut { get; }

    public ShellCommand Copy { get; }

    public ShellCommand Duplicate { get; }

    public ShellCommand Entity { get; }

    public ShellCommand Delete { get; }

    public ShellCommand SelectAll { get; }

    public ShellCommand SelectNone { get; }

    public ShellCommand Wireframe { get; }

    public ShellCommand Grid { get; }

    public ShellCommand Gizmos { get; }

    public ShellCommand MoveQuickAccess { get; }

    public ShellCommand AlignLeft { get; }

    public ShellCommand Center { get; }

    public ShellCommand Distribute { get; }

    public ShellCommand Group { get; }

    public ShellCommand Forward { get; }

    public ShellCommand Backward { get; }

    public ShellCommand AddLight { get; }

    public ShellCommand Bake { get; }

    public ShellCommand Collider { get; }

    public ShellCommand Simulate { get; }

    public ShellCommand Measure { get; }

    public ShellCommand Annotate { get; }

    public ShellCommand Add { get; }

    public ShellCommand Cube { get; }

    public ShellCommand Sphere { get; }

    public ShellCommand Plane { get; }

    public ShellCommand Extrude { get; }

    public ShellCommand Bevel { get; }

    public ShellCommand Subdivide { get; }

    public ShellCommand Snap { get; }

    public ShellCommand SnapToVertices { get; }

    public ShellCommand SnapToEdges { get; }

    public ShellCommand SnapToFaces { get; }

    public ShellCommand NewMaterial { get; }

    public ShellCommand Albedo { get; }

    public ShellCommand Normal { get; }

    public ShellCommand Roughness { get; }

    public ShellCommand Unwrap { get; }

    /// <summary>Every command, in the order the page lists them.</summary>
    public IReadOnlyList<ShellCommand> All { get; }
}
