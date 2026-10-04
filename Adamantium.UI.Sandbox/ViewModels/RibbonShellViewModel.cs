using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.Core.Commands;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Sandbox.Localization;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.Modules;

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

    [Bindable] private double _gridSize = 1.0;

    /// <summary>Read by BOTH quick-access bars - the one in the caption and the one in the ribbon's footer row. Each
    /// shows itself only while this names its own slot; the collection they list is this one view model's.</summary>
    [Bindable] private RibbonQuickAccessPlacement _quickAccessPlacement = RibbonQuickAccessPlacement.Caption;

    /// <summary>Which tab's commands the customize page offers.</summary>
    [Bindable] private ShellTab _commandSource = ShellTab.All;

    /// <summary>The command picked among those the page offers.</summary>
    [Bindable, Affects(nameof(AddToBarCommand))] private ShellCommand _selectedChoice;

    /// <summary>The command picked in the bar's list on the page.</summary>
    [Bindable, Affects(nameof(RemoveFromBarCommand), nameof(MoveUpInBarCommand), nameof(MoveDownInBarCommand))]
    private ShellCommand _selectedBarItem;

    [Bindable] private ShadingMode _shadingMode = ShadingMode.Lit;

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

    [Command] private void Unwrap() => LastAction = RibbonAction.Unwrapped;

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

    /// <summary>The module catalog is out - the drawer the ribbon opens in place of its band.</summary>
    [Bindable] private bool _isModuleCatalogOpen;

    [Bindable] private ModuleCatalogFilter _catalogFilter = ModuleCatalogFilter.All;

    [Bindable] private ModuleSection _catalogSection = ModuleSection.All;

    [Bindable] private string _catalogSearch = string.Empty;

    /// <summary>The module picked in the catalog's list; null while the filter keeps the pick out of it.</summary>
    [Bindable] private EditorModule _selectedModule;

    /// <summary>The module the catalog tells about: the last one picked, kept when a filter takes it out of the list - a
    /// module updated under "Updates" leaves that list, and is still the one being looked at.</summary>
    [Bindable] private EditorModule _shownModule;

    private ObservableCollection<EditorModule> _modules;

    private readonly Dictionary<EditorModule, ModulesMenuRow> _moduleRows = [];

    private IReadOnlyList<ModulesMenuRow> _menuTail;

    private ShellCommands _commands;

    private ObservableCollection<ShellCommand> _quickAccess;

    public RibbonShellViewModel()
    {
        foreach (var module in Modules)
        {
            Take(module);
        }

        CatalogModules = new CollectionView(Modules)
        {
            Filter = module => Fits((EditorModule)module),
            IsLiveFiltering = true,
            LiveFilteringProperties = { nameof(EditorModule.State) }
        };
        SelectedModule = Surface;
        ShowModulesMenu();

        QuickAccess.CollectionChanged += OnQuickAccessChanged;
        ShowChoices();
    }

    /// <summary>Every command of the shell, as data: the ribbon's buttons, the quick-access bar and the customize page all
    /// draw from it. Built on first read - the commands it runs are generated, so they exist by then.</summary>
    public ShellCommands Commands => _commands ??= new ShellCommands(this);

    /// <summary>The quick-access bar: commands of <see cref="Commands"/>, in the order the user put them. Both bars and
    /// the ribbon read it; only the customize page and the ribbon's requests change it.</summary>
    public ObservableCollection<ShellCommand> QuickAccess => _quickAccess ??= [.. DefaultQuickAccess];

    /// <summary>The tabs the customize page can offer commands from.</summary>
    public IReadOnlyList<ShellTab> CommandSources { get; } =
    [
        ShellTab.All, ShellTab.Home, ShellTab.Modeling, ShellTab.Materials, ShellTab.View, ShellTab.Geometry, ShellTab.Uv,
        ShellTab.Light
    ];

    /// <summary>The commands the customize page offers: those of <see cref="CommandSource"/> not in the bar yet.</summary>
    public ObservableCollection<ShellCommand> CommandChoices { get; } = [];

    /// <summary>The bar below the ribbon rather than in the caption.</summary>
    public bool IsQuickAccessBelow
    {
        get => QuickAccessPlacement == RibbonQuickAccessPlacement.BelowRibbon;
        set => QuickAccessPlacement = value ? RibbonQuickAccessPlacement.BelowRibbon : RibbonQuickAccessPlacement.Caption;
    }

    public bool HasChoice => SelectedChoice != null;

    public bool HasBarItem => SelectedBarItem != null;

    public bool CanMoveUpInBar => HasBarItem && QuickAccess.IndexOf(SelectedBarItem) > 0;

    public bool CanMoveDownInBar => HasBarItem && QuickAccess.IndexOf(SelectedBarItem) < QuickAccess.Count - 1;

    private IReadOnlyList<ShellCommand> DefaultQuickAccess => [Commands.Save, Commands.Undo, Commands.Redo];

    partial void OnCommandSourceChanged(ShellTab value) => ShowChoices();

    partial void OnQuickAccessPlacementChanged(RibbonQuickAccessPlacement value) =>
        RaisePropertyChanged(nameof(IsQuickAccessBelow));

    private void OnQuickAccessChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ShowChoices();
        MoveUpInBarCommand.RaiseCanExecuteChanged();
        MoveDownInBarCommand.RaiseCanExecuteChanged();
    }

    private void ShowChoices()
    {
        Show(CommandChoices, Commands.All
            .Where(command => !QuickAccess.Contains(command))
            .Where(command => CommandSource == ShellTab.All || command.Tabs.HasFlag(CommandSource))
            .ToList());
    }

    [Command(CanExecute = nameof(HasChoice))] private void AddToBar()
    {
        var command = SelectedChoice;
        PutInBar(command);
        SelectedBarItem = command;
    }

    [Command(CanExecute = nameof(HasBarItem))] private void RemoveFromBar()
    {
        var index = QuickAccess.IndexOf(SelectedBarItem);
        TakeOutOfBar(SelectedBarItem);
        SelectedBarItem = QuickAccess.Count == 0 ? null : QuickAccess[Math.Min(index, QuickAccess.Count - 1)];
    }

    [Command(CanExecute = nameof(CanMoveUpInBar))] private void MoveUpInBar() => MoveInBar(-1);

    [Command(CanExecute = nameof(CanMoveDownInBar))] private void MoveDownInBar() => MoveInBar(1);

    private void MoveInBar(int step)
    {
        var command = SelectedBarItem;
        var index = QuickAccess.IndexOf(command);
        QuickAccess.Move(index, index + step);
        SelectedBarItem = command;
        LastAction = RibbonAction.QuickAccessReordered;
    }

    [Command] private void ResetQuickAccess()
    {
        QuickAccess.Clear();
        foreach (var command in DefaultQuickAccess)
        {
            QuickAccess.Add(command);
        }

        SelectedBarItem = null;
        LastAction = RibbonAction.QuickAccessReset;
    }

    private void PutInBar(ShellCommand command)
    {
        if (QuickAccess.Contains(command))
        {
            return;
        }

        QuickAccess.Add(command);
        LastAction = RibbonAction.AddedToQuickAccess;
    }

    private void TakeOutOfBar(ShellCommand command)
    {
        if (QuickAccess.Remove(command))
        {
            LastAction = RibbonAction.RemovedFromQuickAccess;
        }
    }

    /// <summary>The modules of the document, in the catalog's order - what brings its tabs to the ribbon.</summary>
    public ObservableCollection<EditorModule> DocumentModules { get; } = [];

    /// <summary>The rows of the "Modules" drop-down: every module of the document, then the catalog and the sets.</summary>
    public ObservableCollection<ModulesMenuRow> ModulesMenu { get; } = [];

    /// <summary>The modules the document is made of, in the "Modules" drop-down next to "File". Each one brings its
    /// own tabs - a context that stays in the strip while the module is in the document and shown.</summary>
    public EditorModule Surface { get; } = new()
    {
        Phrases = RibbonShellStrings.Current,
        Name = nameof(RibbonShellStrings.SurfaceModule),
        Info = nameof(RibbonShellStrings.SurfaceModuleInfo),
        Adds = nameof(RibbonShellStrings.SurfaceAdds),
        Content = nameof(RibbonShellStrings.SurfaceContent),
        Dependents = nameof(RibbonShellStrings.SurfaceDependents),
        Version = "1.2",
        Accent = "#2E8B62",
        Section = ModuleSection.World,
        State = EditorModuleState.InDocument
    };

    public EditorModule Space { get; } = new()
    {
        Phrases = RibbonShellStrings.Current,
        Name = nameof(RibbonShellStrings.SpaceModule),
        Info = nameof(RibbonShellStrings.SpaceModuleInfo),
        Adds = nameof(RibbonShellStrings.SpaceAdds),
        Content = nameof(RibbonShellStrings.SpaceContent),
        Dependents = nameof(RibbonShellStrings.SpaceDependents),
        Version = "1.0",
        Accent = "#6A58C9",
        Section = ModuleSection.Space,
        State = EditorModuleState.InDocument,
        IsShown = false
    };

    /// <summary>Every module the editor knows of - those in the document and those it could take.</summary>
    public ObservableCollection<EditorModule> Modules => _modules ??=
    [
        Surface,
        Space,
        new EditorModule
        {
            Phrases = RibbonShellStrings.Current,
            Name = nameof(RibbonShellStrings.InteriorsModule),
            Info = nameof(RibbonShellStrings.InteriorsModuleInfo),
            Adds = nameof(RibbonShellStrings.InteriorsAdds),
            Version = "0.9",
            Accent = "#C98A3E",
            Section = ModuleSection.Buildings,
            State = EditorModuleState.Installed
        },
        new EditorModule
        {
            Phrases = RibbonShellStrings.Current,
            Name = nameof(RibbonShellStrings.CharactersModule),
            Info = nameof(RibbonShellStrings.CharactersModuleInfo),
            Adds = nameof(RibbonShellStrings.CharactersAdds),
            Content = nameof(RibbonShellStrings.CharactersContent),
            Version = "0.8",
            Accent = "#C2557A",
            Section = ModuleSection.Characters,
            State = EditorModuleState.Detached
        },
        new EditorModule
        {
            Phrases = RibbonShellStrings.Current,
            Name = nameof(RibbonShellStrings.StrategyModule),
            Info = nameof(RibbonShellStrings.StrategyModuleInfo),
            Adds = nameof(RibbonShellStrings.StrategyAdds),
            Version = "0.5",
            Accent = "#3F8FBF",
            Section = ModuleSection.Gameplay,
            State = EditorModuleState.Installed
        },
        new EditorModule
        {
            Phrases = RibbonShellStrings.Current,
            Name = nameof(RibbonShellStrings.SoundModule),
            Info = nameof(RibbonShellStrings.SoundModuleInfo),
            Adds = nameof(RibbonShellStrings.SoundAdds),
            Version = "1.0",
            Accent = "#B39A2E",
            Section = ModuleSection.Sound,
            State = EditorModuleState.UpdateAvailable
        },
        new EditorModule
        {
            Phrases = RibbonShellStrings.Current,
            Name = nameof(RibbonShellStrings.VegetationModule),
            Info = nameof(RibbonShellStrings.VegetationModuleInfo),
            Adds = nameof(RibbonShellStrings.VegetationAdds),
            Version = "0.7",
            Accent = "#5E9E3A",
            Section = ModuleSection.World,
            State = EditorModuleState.Available
        },
        new EditorModule
        {
            Phrases = RibbonShellStrings.Current,
            Name = nameof(RibbonShellStrings.WaterModule),
            Info = nameof(RibbonShellStrings.WaterModuleInfo),
            Adds = nameof(RibbonShellStrings.WaterAdds),
            Version = "0.6",
            Accent = "#3A7FC9",
            Section = ModuleSection.World,
            State = EditorModuleState.Available
        },
        new EditorModule
        {
            Phrases = RibbonShellStrings.Current,
            Name = nameof(RibbonShellStrings.DestructionModule),
            Info = nameof(RibbonShellStrings.DestructionModuleInfo),
            Adds = nameof(RibbonShellStrings.DestructionAdds),
            Version = "0.4",
            Accent = "#C0643F",
            Section = ModuleSection.World,
            State = EditorModuleState.Available
        }
    ];

    /// <summary>The modules the catalog lists under its filter, section and search: a live view over
    /// <see cref="Modules"/>, so a module that changes state or arrives from a file finds its place by itself.</summary>
    public CollectionView CatalogModules { get; }

    public IReadOnlyList<ModuleCatalogFilter> CatalogFilters { get; } = Enum.GetValues<ModuleCatalogFilter>();

    public IReadOnlyList<ModuleSection> CatalogSections { get; } = Enum.GetValues<ModuleSection>();

    /// <summary>How many modules are in the document - the count beside "Modules" in the strip.</summary>
    public int ModuleCount => Modules.Count(module => module.IsInDocument);

    partial void OnCatalogFilterChanged(ModuleCatalogFilter value) => CatalogModules.Refresh();

    partial void OnCatalogSectionChanged(ModuleSection value) => CatalogModules.Refresh();

    partial void OnCatalogSearchChanged(string value) => CatalogModules.Refresh();

    partial void OnSelectedModuleChanged(EditorModule value)
    {
        if (value != null)
        {
            ShownModule = value;
        }
    }

    partial void OnIsModuleCatalogOpenChanged(bool value)
    {
        foreach (var module in Modules)
        {
            module.ForgetQuestions();
        }
    }

    private void Take(EditorModule module)
    {
        module.PropertyChanged += OnModuleChanged;
        module.Report = done => LastAction = Enum.TryParse<RibbonAction>(done, out var action)
            ? action
            : RibbonAction.ModuleToolUsed;
    }

    private void OnModuleChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorModule.State))
        {
            return;
        }

        RaisePropertyChanged(nameof(ModuleCount));
        LastAction = RibbonAction.ModulesChanged;
        ShowModulesMenu();
    }

    private void ShowModulesMenu()
    {
        _menuTail ??=
        [
            new ModulesMenuRow { IsSeparator = true },
            new ModulesMenuRow { Title = nameof(RibbonShellStrings.ModuleCatalog), Command = OpenModuleCatalogCommand },
            new ModulesMenuRow
            {
                Title = nameof(RibbonShellStrings.ModuleSets),
                Children =
                [
                    new ModulesMenuRow { Title = nameof(RibbonShellStrings.CoreOnly), Command = UseCoreOnlyCommand },
                    new ModulesMenuRow { Title = nameof(RibbonShellStrings.SurfaceMap), Command = UseSurfaceMapCommand },
                    new ModulesMenuRow { Title = nameof(RibbonShellStrings.StarSystem), Command = UseStarSystemCommand }
                ]
            }
        ];

        var inDocument = Modules.Where(module => module.IsInDocument).ToList();
        Show(DocumentModules, inDocument);
        Show(ModulesMenu, inDocument.Select(RowOf).Concat(_menuTail).ToList());
    }

    private ModulesMenuRow RowOf(EditorModule module)
    {
        if (!_moduleRows.TryGetValue(module, out var row))
        {
            _moduleRows[module] = row = new ModulesMenuRow { Module = module };
        }

        return row;
    }

    private static void Show<T>(ObservableCollection<T> shown, IReadOnlyList<T> wanted)
    {
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(shown[i]))
            {
                shown.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= shown.Count || !Equals(shown[i], wanted[i]))
            {
                shown.Insert(i, wanted[i]);
            }
        }
    }

    private bool Fits(EditorModule module)
    {
        var search = CatalogSearch?.Trim() ?? string.Empty;

        var listed = CatalogFilter switch
        {
            ModuleCatalogFilter.InDocument => module.IsInDocument,
            ModuleCatalogFilter.Installed => module.State != EditorModuleState.Available,
            ModuleCatalogFilter.Updates => module.State == EditorModuleState.UpdateAvailable,
            _ => true
        };
        if (!listed)
        {
            return false;
        }

        if (CatalogSection != ModuleSection.All && module.Section != CatalogSection)
        {
            return false;
        }

        return search.Length == 0
               || Languages.Say(module.Phrases, module.Name).Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || Languages.Say(module.Phrases, module.Info).Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    [Command] private void OpenModuleCatalog()
    {
        IsModuleCatalogOpen = true;
        LastAction = RibbonAction.ModuleCatalogRequested;
    }

    [Command] private void CloseModuleCatalog() => IsModuleCatalogOpen = false;

    [Command] private void InstallModuleFromFile()
    {
        if (!FileDialog.IsAvailable)
        {
            LastAction = RibbonAction.NoFileDialog;
            return;
        }

        var path = FileDialog.Open(new OpenFileRequest
        {
            Title = RibbonShellStrings.InstallFromFile,
            FileTypes = [new FileType(RibbonShellStrings.ModuleFiles, "*.dll")],
            Key = "sandbox.modules.install"
        });
        if (path == null)
        {
            return;
        }

        var loaded = ModuleAssembly.Load(path);
        if (loaded.Failed)
        {
            LastAction = RibbonAction.NotAModule;
            return;
        }

        var fresh = loaded.Modules.Where(module => Modules.All(known => known.GetType().FullName != module.GetType().FullName)).ToList();
        if (fresh.Count == 0)
        {
            LastAction = loaded.Modules.Count == 0 ? RibbonAction.NoModuleInFile : RibbonAction.ModuleAlreadyInstalled;
            return;
        }

        foreach (var module in fresh)
        {
            Take(module);
            Modules.Add(module);
        }

        SelectedModule = fresh[0];
        LastAction = RibbonAction.ModuleInstalled;
    }

    [Command] private void UseCoreOnly() => UseModules(false, false);

    [Command] private void UseSurfaceMap() => UseModules(true, false);

    [Command] private void UseStarSystem() => UseModules(false, true);

    private void UseModules(bool surface, bool space)
    {
        Surface.State = surface ? EditorModuleState.InDocument : EditorModuleState.Installed;
        Surface.IsShown = true;
        Space.State = space ? EditorModuleState.InDocument : EditorModuleState.Installed;
        Space.IsShown = true;
        LastAction = RibbonAction.ModulesChanged;
    }

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

    // A ribbon command is drawn for a command of the catalog, and the request hands that one over - the bar holds the
    // catalog's own commands, so nothing is rebuilt from a description and nothing is kept in step.
    [Command] private void AddToQuickAccess(object request)
    {
        if (request is RibbonQuickAccessEventArgs { Item: ShellCommand command })
        {
            PutInBar(command);
        }
    }

    [Command] private void RemoveFromQuickAccess(object request)
    {
        if (request is RibbonQuickAccessEventArgs { Item: ShellCommand command })
        {
            TakeOutOfBar(command);
        }
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
}
