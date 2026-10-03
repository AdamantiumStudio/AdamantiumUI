using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Sandbox.Localization;
using Adamantium.Win32;

namespace Adamantium.UI.Sandbox.ViewModels;

[ViewModel]
public partial class MainViewModel
{
    private readonly INavigationService _navigation;
    private readonly WindowDemoSettings _settings;
    private readonly IDialogService _dialogs;

    public MainViewModel(INavigationService navigation, WindowDemoSettings settings, IDialogService dialogs)
    {
        _navigation = navigation;
        _settings = settings;
        _dialogs = dialogs;
    }

    // The dialog host follows the Navigation-tab toggle: an in-window overlay, or a separate window.
    private DialogHostKind DialogHost => _settings.DialogsAsWindows ? DialogHostKind.Window : DialogHostKind.Overlay;

    // Shows a confirm dialog (IDialogService), awaits the result and reports it - the VM-first dialog round-trip from a
    // title-bar command, hosted as an overlay or a window per the toggle.
    [Command] 
    private async Task ShowConfirm()
    {
        var result = await _dialogs.ShowDialogAsync<ConfirmDialogViewModel>(
            new NavigationParameters().Add("message", MainStrings.ApplyChanges), DialogHost);
        LastResult = result.Result;
    }

    // The real Help/About: product/version/manufacturer read from the assembly metadata.
    [Command] private Task ShowAbout() => _dialogs.ShowDialogAsync<AboutDialogViewModel>(host: DialogHost);

    // Opens the SECOND window entirely from the view model: name the "workspace" shell (a custom WorkspaceWindow) and the
    // framework creates it and loads WorkspaceShellViewModel's view - this VM never touches a Window type. The
    // single-instance mode follows the shared setting (toggled from the Navigation tab).
    [Command] private Task OpenWorkspace() =>
        _navigation.OpenWindowAsync<WorkspaceShellViewModel>(windowShell: "workspace", singleInstance: !_settings.AllowDuplicateWindows);

    // Its own shell (RibbonWindow), so the caption can carry the quick-access bar.
    [Command] private Task OpenRibbon() =>
        _navigation.OpenWindowAsync<RibbonShellViewModel>(windowShell: "ribbon", singleInstance: true);

    [Command]
    private void ShowMessage()
    {
        Width += 150;
    }

    [Bindable]
    private double _width = 150;

    // Shows the permanent diagnostics PLATE (top-right overlay). Driven by the right-side Diagnostics ToggleSwitch.
    [Bindable] private bool _diagnosticsOpen;

    [Command] private void ToggleDiagnostics() => DiagnosticsOpen = !DiagnosticsOpen;

    // Opens the diagnostics SLIDEPANEL (slides in from the right). Driven by the LEFT caption "Diagnostics" command -
    // kept separate from the plate so the two diagnostics surfaces are toggled independently.
    [Bindable] private bool _diagnosticsPanelOpen;

    [Command] private void ToggleDiagnosticsPanel() => DiagnosticsPanelOpen = !DiagnosticsPanelOpen;

    // Analytic AA (the coverage fringe on fills + feathered strokes), bound to the window's AnalyticAntialiasing. It
    // lives in the diagnostics panel rather than the chrome because it is a comparison switch, not a setting: the flag
    // is re-read per frame, so flipping it re-renders WITHOUT rebuilding the render cache - which is what makes an A/B
    // of an edge artifact possible at all.
    [Bindable] private bool _analyticAa = true;

    // How frames reach the screen, bound to the window's PresentPolicy. Sits next to Analytic AA for the same reason:
    // it is a COMPARISON switch. Immediate takes the presentation back-pressure off the frame loop entirely (measured
    // here: AcquireNextImage 0.6-0.8 ms a frame under Adaptive, 0.01 ms under Immediate), and the plate above shows the
    // difference live. Inherit is offered too, so the application-wide default can be seen for what it is.
    public Adamantium.Graphics.Core.Presentation.PresentPolicy[] PresentPolicies { get; } =
        System.Enum.GetValues<Adamantium.Graphics.Core.Presentation.PresentPolicy>();

    [Bindable] private Adamantium.Graphics.Core.Presentation.PresentPolicy _presentPolicy =
        Adamantium.Graphics.Core.Presentation.PresentPolicy.Inherit;

    /// <summary>Every theme the application has registered, by name - what the theme chooser lists.
    /// <para>Read from the manager each time rather than captured: themes are registered while the application comes
    /// up, and a list captured in the constructor would be the empty one.</para></summary>
    public IReadOnlyList<string> AvailableThemes
    {
        get
        {
            var themes = UIAppContext.Current?.ThemeManager?.Themes;
            if (themes == null) return [];

            var names = new List<string>(themes.Count);
            foreach (var theme in themes) names.Add(theme.Name);
            return names;
        }
    }

    /// <summary>Which theme the chooser shows. Defaults to the one the application is ON, resolved on FIRST READ rather
    /// than in the constructor: a view-model is built while the application is still coming up, when the theme manager
    /// has no themes yet, so a value taken there would be null for good and the chooser would open on nothing.</summary>
    public string SelectedThemeName
    {
        get => _selectedThemeName ??= UIAppContext.Current?.ThemeManager?.CurrentTheme?.Name;
        set
        {
            if (_selectedThemeName == value) return;
            _selectedThemeName = value;
            RaisePropertyChanged(nameof(SelectedThemeName));
        }
    }

    private string _selectedThemeName;

    /// <summary>Move the whole application onto the chosen theme. A SWAP, not a variant switch: different style sets and
    /// different metrics, so every template is rebuilt - unlike the Dark / Light button beside it, whose cost is a color
    /// write per palette key.</summary>
    [Command]
    private void SwitchTheme()
    {
        var manager = UIAppContext.Current?.ThemeManager;
        if (manager == null || string.IsNullOrEmpty(SelectedThemeName)) return;

        var theme = manager[SelectedThemeName];
        if (theme != null && !ReferenceEquals(theme, manager.CurrentTheme)) manager.SetTheme(theme);
    }

    /// <summary>The languages the application's strings are written in - what the language chooser lists.</summary>
    public IReadOnlyList<string> AvailableLanguages => Languages.Available;

    /// <summary>The application's language. Every string of the framework, and every number and date a binding writes,
    /// follow it while the application runs.</summary>
    public string Language
    {
        get => Languages.Current;
        set
        {
            if (string.IsNullOrEmpty(value) || value == Languages.Current) return;
            Languages.Current = value;
            RaisePropertyChanged(nameof(Language));
        }
    }

    // Last dialog result - shown in the window content (written by ShowConfirm); none before the first.
    [Bindable] private DialogButtonResult? _lastResult;

    // Window resize mode (bound two-way to Window.ResizeMode) + a toggle between full edge resize and grip-only resize
    // (the borderless ResizeGripper in the bottom-right corner).
    [Bindable] private WindowResizeMode _windowResizeMode = WindowResizeMode.CanResize;

    [Command] private void ToggleGripResize() =>
        WindowResizeMode = WindowResizeMode == WindowResizeMode.CanResizeWithGrip
            ? WindowResizeMode.CanResize
            : WindowResizeMode.CanResizeWithGrip;

    // Right-side caption command bar (bound to Window.RightWindowCommands). Deliberately several items so they overflow
    // into the "..." menu when the window is narrowed - the resize-smaller use-case. Built lazily so the generated
    // commands exist by first bind.
    // Caption commands as VECTOR icons (SVG-style stroked paths in a ~14x14 box) + hover tooltips - the modern look.
    private List<WindowCommand> _rightWindowCommands;
    public IEnumerable RightWindowCommands => _rightWindowCommands ??= new()
    {
        new WindowCommand { IconData = "M1,2 L13,2 L13,12 L1,12 Z M1,5 L13,5",     Label = MainStrings.Workspace, ToolTip = MainStrings.OpenWorkspace, Command = OpenWorkspaceCommand },
        new WindowCommand { IconData = "M1,1 L13,1 L13,13 L1,13 Z M1,4 L13,4 M4,4 L4,1 M3,7 L6,7 M3,10 L6,10 M8,6 L12,6 L12,11 L8,11 Z", Label = MainStrings.Ribbon, ToolTip = MainStrings.OpenRibbon, Command = OpenRibbonCommand },
        new WindowCommand { IconData = "M1,2 L13,2 L13,12 L1,12 Z M4,6 L10,6 M4,9 L8,9", Label = MainStrings.Dialog, ToolTip = MainStrings.ShowConfirm, Command = ShowConfirmCommand },
        new WindowCommand { IconData = "M7,0 L14,7 L7,14 L0,7 Z",                  Label = MainStrings.Help, ToolTip = MainStrings.AboutSandbox, Command = ShowAboutCommand },
    };

    private List<WindowCommand> _leftWindowCommands;
    public IEnumerable LeftWindowCommands => _leftWindowCommands ??= new()
    {
        new WindowCommand { IconData = "M1,13 L1,7 M6,13 L6,2 M11,13 L11,9",       Label = MainStrings.Diagnostics, ToolTip = MainStrings.ToggleDiagnostics, Command = ToggleDiagnosticsPanelCommand },
        // Grip-only resize toggle - a diagonal resize double-arrow.
        new WindowCommand { IconData = "M2,2 L12,12 M12,12 L12,8 M12,12 L8,12 M2,2 L2,6 M2,2 L6,2", Label = MainStrings.ResizeMode, ToolTip = MainStrings.ToggleGripResize, Command = ToggleGripResizeCommand },
    };
}