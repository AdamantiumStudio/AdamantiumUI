using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Localization;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Sandbox.Localization;
using Adamantium.UI.Sandbox.Canvases.ViewModels;
using Adamantium.MVVM;
using Adamantium.UI.Controls;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Root view-model of the control gallery: one child view-model per tab, exposed as a single collection the
/// TabControl binds to through a filtered view (<c>ItemsSource="{Binding FoundTabs}"</c>). Each tab renders its header via the TabControl's
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
        new ToolBarsViewModel(),
        new TreesViewModel(),
        new SplitterViewModel(),
        new ColorPickerViewModel(),
        new DateTimeViewModel(),
        new RangesViewModel(),
        new KeyboardViewModel(),
        new ResourcesViewModel(),
        new MarkupViewModel(),
        new ScrollBarViewModel(),
        new ListsViewModel(),
        new MultiTouchViewModel(),
        new ShapesViewModel(),
        new LoadersViewModel(),
        new AnimationsViewModel(),
        new SlidePanelViewModel(),
        new ImageViewModel(),
        new VectorIconsViewModel(),
        new ClippingViewModel(),
        new PdfDrawingViewModel(),
        new PdfPageViewModel(),
        new OpacityViewModel(),
        new ViewboxViewModel(),
        new ZoomBoxViewModel(),
        new DrawingBoardViewModel(),
        new NodeGraphViewModel(),
        new TilesViewModel(),
        new InstancingViewModel(),
        new TransformsViewModel(),
    };

    /// <summary>The tabs the strip shows: those whose title has what <see cref="TabSearch"/> asks for.</summary>
    public CollectionView FoundTabs { get; }

    [Bindable] private TabPageViewModel _selectedTab;

    // What the field over the strip looks for in the tabs' titles; empty shows them all.
    [Bindable] private string _tabSearch;

    // Drives the TabControl.TabStripPlacement from a DropDown (enum-bound) so the strip can move to any edge live.
    [Bindable] private TabStripPlacement _tabPlacement = TabStripPlacement.Left;

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
        Tabs.Insert(Tabs.IndexOf(Tabs.First(t => t is ClippingViewModel)), resolver.Resolve<LayoutViewModel>());
        // Printing shows its dialog through the print service: from DI too, right after the PDF page.
        Tabs.Insert(Tabs.IndexOf(Tabs.First(t => t is PdfPageViewModel)) + 1, resolver.Resolve<PrintingViewModel>());
        Tabs.Add(resolver.Resolve<WebPageViewModel>());
        Tabs.Add(resolver.Resolve<SceneViewModel>());
        SelectedTab = Tabs[2];
        FoundTabs = new CollectionView(Tabs) { Filter = tab => IsFound((TabPageViewModel)tab) };

        // The gallery lives as long as the application.
        Languages.Changed += (_, _) => FoundTabs.Refresh();
    }

    partial void OnTabSearchChanged(string value) => FoundTabs.Refresh();

    // In the application's language, as the titles are said.
    private bool IsFound(TabPageViewModel tab)
    {
        var title = Languages.Say(GalleryStrings.Current, tab.Header);
        return Languages.Culture.CompareInfo.IndexOf(title, TabSearch?.Trim() ?? string.Empty, CompareOptions.IgnoreCase) >= 0;
    }
}
