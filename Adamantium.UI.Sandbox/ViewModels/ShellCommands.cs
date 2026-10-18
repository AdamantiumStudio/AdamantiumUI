using System.Collections.Generic;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Every command of the shell, each under its own name, and all of them in <see cref="All"/> for the customize
/// page.</summary>
public sealed class ShellCommands
{
    public ShellCommands(RibbonShellViewModel shell)
    {
        Save = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Save),
            IconKey = "SaveIcon",
            Command = shell.SaveCommand
        };
        Undo = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Undo),
            IconKey = "UndoIcon",
            Command = shell.UndoCommand
        };
        Redo = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Redo),
            IconKey = "RedoIcon",
            Command = shell.RedoCommand
        };

        Paste = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Paste),
            Tabs = ShellTab.Home,
            IconKey = "PasteIcon",
            Command = ApplicationCommands.Paste,
            DropDownItems = shell.PasteOptions
        };
        Cut = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Cut),
            Tabs = ShellTab.Home,
            IconKey = "CutIcon",
            Command = ApplicationCommands.Cut
        };
        Copy = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Copy),
            Tabs = ShellTab.Home,
            IconKey = "CopyIcon",
            Command = ApplicationCommands.Copy
        };
        Duplicate = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Duplicate),
            Tabs = ShellTab.Home,
            IconKey = "DuplicateIcon",
            Command = shell.DuplicateCommand
        };
        Entity = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Entity),
            Tabs = ShellTab.Home,
            IconKey = "EntityIcon",
            Command = shell.AddEntityCommand
        };
        Delete = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Delete),
            Tabs = ShellTab.Home | ShellTab.View,
            IconKey = "DeleteIcon",
            Command = shell.DeleteCommand
        };
        SelectAll = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SelectAll),
            Tabs = ShellTab.Home | ShellTab.View,
            IconKey = "SelectAllIcon",
            Command = ApplicationCommands.SelectAll
        };
        SelectNone = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SelectNone),
            Tabs = ShellTab.Home | ShellTab.View,
            IconKey = "SelectNoneIcon",
            Command = shell.SelectNoneCommand
        };
        Wireframe = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Wireframe),
            Tabs = ShellTab.Home | ShellTab.Materials | ShellTab.View,
            IconKey = "WireframeIcon",
            IsChecked = false
        };
        Grid = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Grid),
            Tabs = ShellTab.Home | ShellTab.View,
            IconKey = "GridIcon",
            IsChecked = true
        };
        Gizmos = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Gizmos),
            Tabs = ShellTab.Home | ShellTab.View,
            IconKey = "GizmosIcon",
            IsChecked = true
        };
        MoveQuickAccess = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.MoveQuickAccess),
            Tabs = ShellTab.Home,
            IconKey = "MoveQuickAccessIcon",
            Command = shell.MoveQuickAccessCommand
        };
        AlignLeft = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.AlignLeft),
            Tabs = ShellTab.Home,
            IconKey = "AlignLeftIcon",
            Command = shell.AlignLeftCommand
        };
        Center = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Center),
            Tabs = ShellTab.Home,
            IconKey = "CenterIcon",
            Command = shell.AlignCenterCommand
        };
        Distribute = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Distribute),
            Tabs = ShellTab.Home,
            IconKey = "DistributeIcon",
            Command = shell.DistributeCommand
        };
        Group = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Group),
            Tabs = ShellTab.Home,
            IconKey = "GroupIcon",
            Command = shell.GroupSelectionCommand
        };
        Forward = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Forward),
            Tabs = ShellTab.Home,
            IconKey = "ForwardIcon",
            Command = shell.BringForwardCommand
        };
        Backward = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Backward),
            Tabs = ShellTab.Home,
            IconKey = "BackwardIcon",
            Command = shell.SendBackwardCommand
        };
        AddLight = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.AddLight),
            Tabs = ShellTab.Home | ShellTab.Light,
            IconKey = "AddLightIcon",
            Command = shell.AddLightCommand
        };
        Bake = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Bake),
            Tabs = ShellTab.Home | ShellTab.Light,
            IconKey = "BakeLightingIcon",
            Command = shell.BakeLightingCommand
        };
        Collider = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Collider),
            Tabs = ShellTab.Home,
            IconKey = "ColliderIcon",
            Command = shell.AddColliderCommand
        };
        Simulate = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Simulate),
            Tabs = ShellTab.Home,
            IconKey = "SimulateIcon",
            Command = shell.SimulateCommand
        };
        Measure = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Measure),
            Tabs = ShellTab.Home | ShellTab.Uv,
            IconKey = "MeasureIcon",
            Command = shell.MeasureCommand
        };
        Annotate = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Annotate),
            Tabs = ShellTab.Home,
            IconKey = "AnnotateIcon",
            Command = shell.AnnotateCommand
        };

        Add = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Add),
            Tabs = ShellTab.Modeling,
            IconKey = "AddIcon",
            DropDownItems = shell.PrimitiveOptions
        };
        Cube = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Cube),
            Tabs = ShellTab.Modeling,
            IconKey = "CubeIcon",
            Command = shell.AddCubeCommand
        };
        Sphere = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Sphere),
            Tabs = ShellTab.Modeling,
            IconKey = "SphereIcon",
            Command = shell.AddSphereCommand
        };
        Plane = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Plane),
            Tabs = ShellTab.Modeling,
            IconKey = "PlaneIcon",
            Command = shell.AddPlaneCommand
        };
        Extrude = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Extrude),
            Tabs = ShellTab.Modeling | ShellTab.Geometry,
            IconKey = "ExtrudeIcon",
            Command = shell.ExtrudeCommand
        };
        Bevel = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Bevel),
            Tabs = ShellTab.Modeling | ShellTab.Geometry,
            IconKey = "BevelIcon",
            Command = shell.BevelCommand
        };
        Subdivide = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Subdivide),
            Tabs = ShellTab.Modeling | ShellTab.Geometry,
            IconKey = "SubdivideIcon",
            Command = shell.SubdivideCommand
        };
        Snap = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Snap),
            Tabs = ShellTab.Modeling,
            IconKey = "SnapIcon",
            IsChecked = false
        };
        SnapToVertices = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SnapToVertices),
            Tabs = ShellTab.Modeling,
            IconKey = "SnapToVerticesIcon",
            IsChecked = false
        };
        SnapToEdges = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SnapToEdges),
            Tabs = ShellTab.Modeling,
            IconKey = "SnapToEdgesIcon",
            IsChecked = false
        };
        SnapToFaces = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.SnapToFaces),
            Tabs = ShellTab.Modeling,
            IconKey = "SnapToFacesIcon",
            IsChecked = false
        };

        NewMaterial = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.NewMaterial),
            Tabs = ShellTab.Materials,
            IconKey = "NewMaterialIcon",
            Command = shell.NewMaterialCommand
        };
        Albedo = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Albedo),
            Tabs = ShellTab.Materials,
            IconKey = "AlbedoIcon",
            Command = shell.EditAlbedoCommand
        };
        Normal = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Normal),
            Tabs = ShellTab.Materials,
            IconKey = "NormalIcon",
            Command = shell.EditNormalCommand
        };
        Roughness = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Roughness),
            Tabs = ShellTab.Materials,
            IconKey = "RoughnessIcon",
            Command = shell.EditRoughnessCommand
        };

        Unwrap = new ShellCommand
        {
            Name = nameof(RibbonShellStrings.Unwrap),
            Tabs = ShellTab.Uv,
            IconKey = "UnwrapUvIcon",
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

        MeshTools = [Extrude, Bevel, Subdivide];
    }

    /// <summary>The mesh group's commands, given to it as data rather than written out as buttons.</summary>
    public IReadOnlyList<ShellCommand> MeshTools { get; }

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
