using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Adamantium.Core.Commands;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Content view model of the RIBBON window: a command band over a document surface, with the quick-access bar
/// in the caption. Names its window shell through <see cref="IWindowAware"/>, so it never touches a Window type.</summary>
[ViewModel]
public partial class RibbonShellViewModel : IWindowAware
{
    public string WindowShellKey => "ribbon";
    public string Title => RibbonShellStrings.Title;
    public double Width => 1180;
    public double Height => 720;

    /// <summary>The demo's stand-in for a document: what was done last, which the status line says.</summary>
    [Bindable] private RibbonAction _lastAction = RibbonAction.Ready;

    /// <summary>The file format the last import or export was in.</summary>
    [Bindable] private string _format;

    // This window has its OWN swapchain, so its presentation is its own setting - which is the point of comparing the
    // two shells side by side: one can run unthrottled while the other stays tear-free, in one process.
    public Adamantium.Graphics.Core.Presentation.PresentPolicy[] PresentPolicies { get; } =
        Enum.GetValues<Adamantium.Graphics.Core.Presentation.PresentPolicy>();

    [Bindable] private Adamantium.Graphics.Core.Presentation.PresentPolicy _presentPolicy =
        Adamantium.Graphics.Core.Presentation.PresentPolicy.Inherit;

    /// <summary>Gates Cut/Copy/Delete/Duplicate - toggle it in the View tab and half the Home tab goes dim.</summary>
    [Bindable, Affects(nameof(CutCommand), nameof(CopyCommand), nameof(DeleteCommand), nameof(DuplicateCommand))]
    private bool _hasSelection = true;

    /// <summary>Set by Cut/Copy; gates Paste.</summary>
    [Bindable, Affects(nameof(PasteCommand))] private bool _hasClipboard;

    // Two-way bound to RibbonToggleButtons, read back by the document surface.
    [Bindable] private bool _showGrid = true;
    [Bindable] private bool _showGizmos = true;
    [Bindable] private bool _wireframe;
    [Bindable] private bool _snapToGrid;

    [Bindable] private double _gridSize = 1.0;

    /// <summary>Read by BOTH quick-access bars - the one in the caption and the one in the ribbon's footer row. Each
    /// shows itself only while this names its own slot; the collection they list is this one view model's.</summary>
    [Bindable] private RibbonQuickAccessPlacement _quickAccessPlacement = RibbonQuickAccessPlacement.Caption;

    [Bindable] private ShadingMode _shadingMode = ShadingMode.Lit;

    /// <summary>An icon held as DATA - plain path text, converted to a Geometry by the binding.</summary>
    public string MaterialIcon => "M3,2 L11,2 L13,4 L13,14 L3,14 Z M8,6 L8,11 M5.5,8.5 L10.5,8.5";

    public IEnumerable<ShadingMode> ShadingModes { get; } = Enum.GetValues<ShadingMode>();

    // The gates name the generated [Bindable] properties directly - both halves come out of one generator pass.
    [Command(CanExecute = nameof(HasClipboard))] private void Paste() => LastAction = RibbonAction.Pasted;

    [Command] private void PasteKeepFormatting() => LastAction = RibbonAction.PastedKeepingFormatting;

    [Command] private void PasteValuesOnly() => LastAction = RibbonAction.PastedValuesOnly;

    [Command] private void PasteSpecial() => LastAction = RibbonAction.PasteSpecial;

    /// <summary>The rows of Paste's drop-down, as DATA - which is what lets the command be put in the quick-access bar
    /// and keep its arrow there. Built on first read: the commands are generated, so they exist by then.</summary>
    public IReadOnlyList<MenuCommand> PasteOptions => _pasteOptions ??=
    [
        new MenuCommand { Header = nameof(RibbonShellStrings.KeepFormatting), Command = PasteKeepFormattingCommand },
        new MenuCommand { Header = nameof(RibbonShellStrings.ValuesOnly), Command = PasteValuesOnlyCommand },
        new MenuCommand { Header = nameof(RibbonShellStrings.PasteSpecial), Command = PasteSpecialCommand }
    ];

    private IReadOnlyList<MenuCommand> _pasteOptions;

    /// <summary>The rows of Add's drop-down - data for the same reason.</summary>
    public IReadOnlyList<MenuCommand> PrimitiveOptions => _primitiveOptions ??=
    [
        new MenuCommand { Header = nameof(RibbonShellStrings.Cube), Command = AddCubeCommand },
        new MenuCommand { Header = nameof(RibbonShellStrings.Sphere), Command = AddSphereCommand },
        new MenuCommand { Header = nameof(RibbonShellStrings.Plane), Command = AddPlaneCommand },
        new MenuCommand { Header = nameof(RibbonShellStrings.EmptyEntity), Command = AddEntityCommand }
    ];

    private IReadOnlyList<MenuCommand> _primitiveOptions;

    /// <summary>The rows of the right-click menu THIS shell wants on its snapping commands, instead of the one the
    /// ribbon offers. Nothing about them is the ribbon's business - they are the view model's own list, drawn by the
    /// template the view points <c>Ribbon.CommandContextMenuTemplate</c> at.</summary>
    public IReadOnlyList<MenuCommand> SnapMenuRows => _snapMenuRows ??=
    [
        new MenuCommand { Header = nameof(RibbonShellStrings.SnapSettings), Command = SnapSettingsCommand },
        new MenuCommand { Header = nameof(RibbonShellStrings.ClearSnaps), Command = ClearSnapsCommand }
    ];

    private IReadOnlyList<MenuCommand> _snapMenuRows;

    [Command] private void SnapSettings() => LastAction = RibbonAction.SnapSettings;

    [Command] private void ClearSnaps() => LastAction = RibbonAction.SnapsCleared;

    [Command(CanExecute = nameof(HasSelection))] private void Cut()
    {
        HasClipboard = true;
        LastAction = RibbonAction.Cut;
    }

    [Command(CanExecute = nameof(HasSelection))] private void Copy()
    {
        HasClipboard = true;
        LastAction = RibbonAction.Copied;
    }

    [Command(CanExecute = nameof(HasSelection))] private void Delete()
    {
        HasSelection = false;
        LastAction = RibbonAction.Deleted;
    }

    [Command(CanExecute = nameof(HasSelection))] private void Duplicate() => LastAction = RibbonAction.Duplicated;

    [Command] private void SelectAll()
    {
        HasSelection = true;
        LastAction = RibbonAction.SelectedAll;
    }

    [Command] private void SelectNone()
    {
        HasSelection = false;
        LastAction = RibbonAction.SelectionCleared;
    }

    [Command] private void AddEntity() => LastAction = RibbonAction.AddedEntity;

    [Command] private void AddCube() => Add(RibbonAction.AddedCube);

    [Command] private void AddSphere() => Add(RibbonAction.AddedSphere);

    [Command] private void AddPlane() => Add(RibbonAction.AddedPlane);

    [Command] private void Extrude() => LastAction = RibbonAction.Extruded;

    [Command] private void Bevel() => LastAction = RibbonAction.Bevelled;

    [Command] private void Subdivide() => LastAction = RibbonAction.Subdivided;

    [Command] private void NewMaterial() => LastAction = RibbonAction.NewMaterial;

    [Command] private void EditAlbedo() => LastAction = RibbonAction.EditingAlbedo;

    [Command] private void EditNormal() => LastAction = RibbonAction.EditingNormal;

    [Command] private void EditRoughness() => LastAction = RibbonAction.EditingRoughness;

    /// <summary>The gallery's choices. DATA, so the dropped-down gallery can build its own cells from the same template.</summary>
    private static readonly MaterialSwatch[] MaterialChoices =
    [
        new MaterialSwatch { Name = "Steel", Fill = "#8A8F98" },
        new MaterialSwatch { Name = "Copper", Fill = "#B87333" },
        new MaterialSwatch { Name = "Gold", Fill = "#D4AF37" },
        new MaterialSwatch { Name = "Jade", Fill = "#4FA37A" },
        new MaterialSwatch { Name = "Cobalt", Fill = "#3B6FD4" },
        new MaterialSwatch { Name = "Ruby", Fill = "#C0334A" },
        new MaterialSwatch { Name = "Slate", Fill = "#4A5058" },
        new MaterialSwatch { Name = "Sand", Fill = "#C9B48A" },
        new MaterialSwatch { Name = "Ivory", Fill = "#E8E2D4" },
        new MaterialSwatch { Name = "Basalt", Fill = "#2E3238" },
        new MaterialSwatch { Name = "Moss", Fill = "#6B7A45" },
        new MaterialSwatch { Name = "Plum", Fill = "#7A4A85" }
    ];

    public IReadOnlyList<MaterialSwatch> Materials => MaterialChoices;

    [Bindable] private MaterialSwatch _selectedMaterial = MaterialChoices[0];

    /// <summary>What the contextual tabs hang on: switch it and "Mesh tools" appears in the strip with its own tabs.
    /// The ribbon only reads it - appearing is an offer, so the open tab is not pulled out from under anyone.</summary>
    [Bindable] private bool _hasMeshSelection;

    /// <summary>A second context, so the strip has to order two of them and draw two ledges.</summary>
    [Bindable] private bool _hasLightSelection;

    /// <summary>Whether the contexts draw their ledge. Off, the color of the tabs is the only thing saying which
    /// belong together - and the strip stops paying the ledge row's height.</summary>
    [Bindable] private bool _showContextHeader = true;

    // Home carries a real editor's worth of groups, so the band's LAST resort - scrolling, once every group has been
    // collapsed and it still does not fit - is reachable by dragging the window narrow.
    [Command] private void AlignLeft() => LastAction = RibbonAction.AlignedLeft;

    [Command] private void AlignCenter() => LastAction = RibbonAction.Centered;

    [Command] private void Distribute() => LastAction = RibbonAction.Distributed;

    [Command] private void GroupSelection() => LastAction = RibbonAction.Grouped;

    [Command] private void BringForward() => LastAction = RibbonAction.BroughtForward;

    [Command] private void SendBackward() => LastAction = RibbonAction.SentBackward;

    [Command] private void AddLight() => LastAction = RibbonAction.AddedLight;

    [Command] private void BakeLighting() => LastAction = RibbonAction.Baking;

    [Command] private void AddCollider() => LastAction = RibbonAction.AddedCollider;

    [Command] private void Simulate() => LastAction = RibbonAction.Simulating;

    [Command] private void Measure() => LastAction = RibbonAction.Measuring;

    [Command] private void Annotate() => LastAction = RibbonAction.Annotating;

    // The ribbon hands over a DESCRIPTION and never touches this collection - the shell decides what its own items are
    // made of. Here they are WindowCommands, the type the caption bar already lists.
    [Command] private void AddToQuickAccess(object request)
    {
        if (request is not RibbonQuickAccessEventArgs asked) return;


        var item = new QuickAccessCommand
        {
            IconData = asked.Icon as string,
            Label = asked.Label,
            ToolTip = asked.ToolTip as string,
            Key = asked.Key,
            Command = asked.Action,
            CommandParameter = asked.ActionParameter,
            // What is not a button (a slider) hands over its own compact form; a button leaves this null and is drawn
            // by the bar's default.
            QuickAccessTemplate = asked.Template,
            DropDownItems = asked.DropDownItems,
            DropDownItemTemplate = asked.DropDownItemTemplate
        };

        // A command WITH a state is one this view model already keeps a property for, so the item shows THAT property -
        // the button in the caption and the button in the ribbon end up two views of one value. Which command is which is
        // said in the markup by key; nothing here holds a control.
        Mirror(item, asked.Key as string);

        QuickAccess.Add(item);

        // Nothing is written back to the ribbon: it is pointed at this collection (Ribbon.QuickAccessItems in the view)
        // and recognizes its own commands in it by key. A view model that kept the ribbon's control to mark it would be
        // holding a control.
        LastAction = RibbonAction.AddedToQuickAccess;
    }

    // Which of this view model's own states each named command shows. A command that names none stays a plain button.
    private void Mirror(QuickAccessCommand item, string key)
    {
        switch (key)
        {
            case "ShowGrid":
                Mirror(item, nameof(ShowGrid), () => ShowGrid, value => ShowGrid = value);
                break;
            case "ShowGizmos":
                Mirror(item, nameof(ShowGizmos), () => ShowGizmos, value => ShowGizmos = value);
                break;
            case "Wireframe":
                Mirror(item, nameof(Wireframe), () => Wireframe, value => Wireframe = value);
                break;
            case "SnapToGrid":
                Mirror(item, nameof(SnapToGrid), () => SnapToGrid, value => SnapToGrid = value);
                break;
        }
    }

    // ONE value, two views of it: writing either side lands on the property, and the other side is told. No guard is
    // needed - a write that changes nothing raises nothing.
    private void Mirror(QuickAccessCommand item, string property, Func<bool> read, Action<bool> write)
    {
        item.IsChecked = read();
        item.PropertyChanged += (_, _) => write(item.IsChecked == true);

        void Follow(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != property) return;

            item.IsChecked = read();
        }

        PropertyChanged += Follow;
        _mirrors[item] = Follow;
    }

    private readonly Dictionary<QuickAccessCommand, System.ComponentModel.PropertyChangedEventHandler> _mirrors = [];

    [Command] private void RemoveFromQuickAccess(object request)
    {
        if (request is not RibbonQuickAccessEventArgs asked) return;

        for (var i = QuickAccess.Count - 1; i >= 0; i--)
        {
            var item = QuickAccess[i] as QuickAccessCommand;
            var same = asked.Key != null
                ? Equals(item?.Key, asked.Key)
                : item?.Command != null && ReferenceEquals(item.Command, asked.Action);
            if (!same) continue;

            if (_mirrors.Remove(item, out var follow))
            {
                PropertyChanged -= follow;
            }

            QuickAccess.RemoveAt(i);
        }

        LastAction = RibbonAction.RemovedFromQuickAccess;
    }

    [Command] private void MoveQuickAccess()
    {
        var below = QuickAccessPlacement == RibbonQuickAccessPlacement.Caption;
        QuickAccessPlacement = below ? RibbonQuickAccessPlacement.BelowRibbon : RibbonQuickAccessPlacement.Caption;
        LastAction = below ? RibbonAction.QuickAccessBelow : RibbonAction.QuickAccessInCaption;
    }

    /// <summary>Which shape the File menu takes - the window-wide backstage, or the panel dropped under the button.</summary>
    [Bindable] private bool _isBackstage = true;

    [Command] private void Import(object format)
    {
        Format = format as string;
        LastAction = RibbonAction.Imported;
    }

    [Command] private void Export(object format)
    {
        Format = format as string;
        LastAction = RibbonAction.Exported;
    }

    [Command] private void NewScene() => LastAction = RibbonAction.NewScene;

    [Command] private void OpenScene() => LastAction = RibbonAction.OpenedScene;

    [Command] private void Exit() => LastAction = RibbonAction.ExitRequested;

    [Command] private void Save() => LastAction = RibbonAction.Saved;

    [Command] private void Undo() => LastAction = RibbonAction.Undone;

    [Command] private void Redo() => LastAction = RibbonAction.Redone;

    private void Add(RibbonAction added)
    {
        HasSelection = true;
        LastAction = added;
    }

    // On the SHELL, not on either control: the user reorders it and it outlives a session. Lazy, so the generated
    // commands exist by first bind.
    private ObservableCollection<WindowCommand> _quickAccess;

    public ObservableCollection<WindowCommand> QuickAccess => _quickAccess ??=
    [
        new WindowCommand { IconData = "M3,2 L11,2 L13,4 L13,13 L3,13 Z M5,2 L5,6 L11,6 L11,2", Label = RibbonShellStrings.Save, ToolTip = RibbonShellStrings.SaveTip, Command = SaveCommand },
        new WindowCommand { IconData = "M6,4 L2,7 L6,10 M2,7 L10,7 A3,3 0 0 1 10,13 L8,13", Label = RibbonShellStrings.Undo, ToolTip = RibbonShellStrings.Undo, Command = UndoCommand },
        new WindowCommand { IconData = "M8,4 L12,7 L8,10 M12,7 L4,7 A3,3 0 0 0 4,13 L6,13", Label = RibbonShellStrings.Redo, ToolTip = RibbonShellStrings.Redo, Command = RedoCommand },
    ];
}
