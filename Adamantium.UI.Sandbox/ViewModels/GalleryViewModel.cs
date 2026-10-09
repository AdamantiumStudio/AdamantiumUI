using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Sandbox.DrawingBoard.ViewModels;
using Adamantium.MVVM;
using Adamantium.UI.Controls;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Root view-model of the control gallery: one child view-model per tab, exposed as a single collection the
/// TabControl binds to (<c>ItemsSource="{Binding Tabs}"</c>). Each tab renders its header via the TabControl's
/// ItemTemplate ({Binding Header}) and its body via a <see cref="TabViewSelector"/> that maps the tab view-model to its
/// View - so the whole gallery is data-driven, exactly like a real application shell.</summary>
[ViewModel]
public partial class GalleryViewModel
{
    public ObservableCollection<TabPageViewModel> Tabs { get; } = new()
    {
        new ButtonsViewModel(),
        new DataGridViewModel(),
        new PropertyGridViewModel(),
        new TabsViewModel(),
        new MenusViewModel(),
        new TreesViewModel(),
        new SplitterViewModel(),
        new ColorPickerViewModel(),
        new RangesViewModel(),
        new KeyboardViewModel(),
        new ResourcesViewModel(),
        new MarkupViewModel(),
        new ScrollBarViewModel(),
        new ListsViewModel(),
        new ShapesViewModel(),
        new LoadersViewModel(),
        new AnimationsViewModel(),
        new SlidePanelViewModel(),
        new ImageViewModel(),
        new VectorIconsViewModel(),
        new LayoutViewModel(),
        new ClippingViewModel(),
        new OpacityViewModel(),
        new ViewboxViewModel(),
        new ZoomBoxViewModel(),
        new InfiniteCanvasViewModel(),
        new TilesViewModel(),
        new InstancingViewModel(),
        new TransformsViewModel(),
        new SceneViewModel(),
    };

    [Bindable] private TabPageViewModel _selectedTab;

    // Drives the TabControl.TabStripPlacement from a DropDown (enum-bound) so the strip can move to any edge live.
    [Bindable] private TabStripPlacement _tabPlacement = TabStripPlacement.Top;

    // Drives the TabControl.ContentTransition from a DropDown (enum-bound) so the tab-content slide mode switches live.
    [Bindable] private ContentTransition _slideMode = ContentTransition.SlideLeft;

    // Drives the TabControl.SelectionIndicatorPlacement from a DropDown, so the accent bar can be moved between the
    // tab's inner and outer edge live - the two draw the bar in the same place by different alignments, which is the
    // one thing that differs between them.
    [Bindable] private TabIndicatorPlacement _indicatorPlacement = TabIndicatorPlacement.Inner;

    public GalleryViewModel(IDependencyResolver resolver)
    {
        // The Navigation tab owns a region and needs INavigationService - resolve it through DI (which injects the service)
        // rather than newing it here. It's the default tab so the demo opens on it.
        Tabs.Insert(0, resolver.Resolve<NavigationDemoViewModel>());
        // Visual -> Image showcase (RenderTargetBitmap analog): needs IVisualRenderer, so resolve it through DI too.
        Tabs.Insert(1, resolver.Resolve<VisualRenderDemoViewModel>());
        Tabs.Insert(2, resolver.Resolve<DragDropDemoViewModel>());
        // Docking owns a navigation region too (its area IS a region), so it comes from DI as well - put back where it
        // stands in the list above, ahead of Tiles.
        Tabs.Insert(Tabs.IndexOf(Tabs.First(t => t is TilesViewModel)), resolver.Resolve<DockingViewModel>());
        // Text and Brushes own regions too - their topics are separate views navigated into them - so they come from DI,
        // after Animations: Brushes, then Text, just ahead of the slide panel.
        Tabs.Insert(Tabs.IndexOf(Tabs.First(t => t is SlidePanelViewModel)), resolver.Resolve<TextViewModel>());
        Tabs.Insert(Tabs.IndexOf(Tabs.First(t => t is TextViewModel)), resolver.Resolve<BrushesViewModel>());
        SelectedTab = Tabs[2];
    }
}
