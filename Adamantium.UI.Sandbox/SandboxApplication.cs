using System;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls.Navigation;

namespace Adamantium.UI.Sandbox;

// The first window, the module icons and the ribbon shell's styles are stated in AppBlueprint.auml; the build writes
// the entry point from it. Graphics debug (Vulkan validation layers) stays off unless ADAM_VK_DEBUG=1: it costs about
// three quarters of the frame time, so it is a tool for chasing a GPU bug, not a default.
public class SandboxApplication : Adamantium.UI.Universes.MultiverseApplication
{
    public SandboxApplication()
    {
        // A dragged picture should also travel as a file: many targets (Paint 3D, packaged apps) ask for a file list
        // and never look at a bitmap. Off by default in the engine because it writes to disk - an application opts in.
        UI.Input.DragDropOptions.OfferImagesAsFiles = true;

        IsFixedTimeStep = false;
        EnableGraphicsDebug = Environment.GetEnvironmentVariable("ADAM_VK_DEBUG") == "1";
        DesiredFPS = 300;
        this.UseAutomationAgent();
    }

    protected override void RegisterServices(IContainerRegistry containerRegistry)
    {
        base.RegisterServices(containerRegistry);
        // Register the app's own window shell under "workspace"; the Workspace command opens the second window in it.
        Container.Resolve<IWindowShellRegistry>().Register<WorkspaceWindow>("workspace");
        // A dedicated shell for the drag-drop cross-window demo, so items can be dragged between it and the main window.
        Container.Resolve<IWindowShellRegistry>().Register<DragDropWindow>("dragdrop");
        // Its caption carries the quick-access bar, so it needs a window of its own.
        Container.Resolve<IWindowShellRegistry>().Register<RibbonWindow>("ribbon");
        // Shared demo setting (single instance) so the title-bar command and the Navigation-tab toggle see the same flag.
        containerRegistry.RegisterInstance<ViewModels.WindowDemoSettings>(new ViewModels.WindowDemoSettings());
        // The brushes tab keeps ONE view-model behind a region: the stands are separate views navigated into it, and
        // every one of them binds to this same object. Resolved rather than constructed here so it still gets its own
        // dependencies, and a singleton so navigating between stands does not build a second copy of the tab's state.
        containerRegistry.RegisterSingleton<ViewModels.BrushesViewModel>();

        // ...and its stands are VIEWS of that one view-model, named by key. This is what the keyed locator is for: the
        // convention maps a view-model to ONE view, and a tab that reads the same object through several files needs
        // several - without inventing an empty view-model per file just to have something to navigate to.
        var views = Container.Resolve<IViewLocator>();
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.GradientStandView>(nameof(ViewModels.LiveStand.Gradients));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.MeshStandView>(nameof(ViewModels.LiveStand.Mesh));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.PatternStandView>(nameof(ViewModels.LiveStand.Pattern));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.NineSliceStandView>(nameof(ViewModels.LiveStand.NineSlice));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.NoiseStandView>(nameof(ViewModels.LiveStand.Noise));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.FractalStandView>(nameof(ViewModels.LiveStand.Fractal));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.ImageStandView>(nameof(ViewModels.LiveStand.Image));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.DrawingStandView>(nameof(ViewModels.LiveStand.Drawing));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.VisualStandView>(nameof(ViewModels.LiveStand.Visual));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.MaterialStandView>(nameof(ViewModels.LiveStand.Material));
        views.RegisterView<ViewModels.BrushesViewModel, Views.Brushes.AuraStandView>(nameof(ViewModels.LiveStand.Aura));
    }
}
