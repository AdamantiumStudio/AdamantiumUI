using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Collections;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Layout tab: a large virtualized wrap grid with adjustable cell width and height and a toggle between rounded
/// rect and ellipse tiles, both SDF-batched; and a stand that holds the loading skeleton cards on screen and tunes their
/// pulse.</summary>
[ViewModel]
public partial class LayoutViewModel : TabPageViewModel
{
    private readonly IRegionManager _regions;

    public LayoutViewModel(IRegionManager regions) : base("Layout")
    {
        _regions = regions;
        Rectangles = new(Enumerable.Range(0, 60000)
            .Select(i => new ColorRect { Number = i + 1, Color = Palette[i % Palette.Length] }));

        // No page size here: the PAGER owns it. It pushes its own into whatever source it is given, so a size stated in
        // both places is a size stated twice, and the one written here would simply be overwritten on attach.
        Paged = new CollectionView(Rectangles);
        TileSource = Rectangles;
        TilePreset = TilePresets[7];
        Fill(GalleryTiles, _galleryCount);
        Fill(FeaturedTiles, _featuredCount);
        Feature(FeaturedTiles[0]);
        ShowFeatured();
        ShowStand(_stand);
    }

    [Bindable] private LayoutStand _stand = LayoutStand.Tiles;

    partial void OnStandChanged(LayoutStand value) => ShowStand(value);

    private void ShowStand(LayoutStand stand)
        => _ = _regions.GetOrCreateRegion(RegionNames.LayoutStand).NavigateToInstanceAsync(this, stand.ToString());

    // ── THE TILED PANEL STAND ─────────────────────────────────────────────────────────────────────────────────────
    // One panel three ways: the viewport layouts of 3D editors, a gallery of any number of tiles, and one featured
    // tile with the rest in a strip.

    /// <summary>Which of the three the stand shows.</summary>
    [Bindable] private TileShowcase _tileShowcase;

    /// <summary>The viewport layouts, each with a thumbnail.</summary>
    public IReadOnlyList<TilePreset> TilePresets { get; } =
    [
        new(nameof(TileLayouts.One), () => TileLayouts.One, Palette),
        new(nameof(TileLayouts.TwoColumns), () => TileLayouts.TwoColumns, Palette),
        new(nameof(TileLayouts.TwoRows), () => TileLayouts.TwoRows, Palette),
        new(nameof(TileLayouts.ThreeLeft), () => TileLayouts.ThreeLeft, Palette),
        new(nameof(TileLayouts.ThreeRight), () => TileLayouts.ThreeRight, Palette),
        new(nameof(TileLayouts.ThreeTop), () => TileLayouts.ThreeTop, Palette),
        new(nameof(TileLayouts.ThreeBottom), () => TileLayouts.ThreeBottom, Palette),
        new(nameof(TileLayouts.Four), () => TileLayouts.Four, Palette),
        new(nameof(TileLayouts.FourLeft), () => TileLayouts.FourLeft, Palette),
        new(nameof(TileLayouts.FourRight), () => TileLayouts.FourRight, Palette),
        new(nameof(TileLayouts.FourTop), () => TileLayouts.FourTop, Palette),
        new(nameof(TileLayouts.FourBottom), () => TileLayouts.FourBottom, Palette)
    ];

    /// <summary>The viewport layout picked.</summary>
    [Bindable] private TilePreset _tilePreset;

    partial void OnTilePresetChanged(TilePreset value)
    {
        ViewportLayout = value?.Make();
        if (value == null)
        {
            return;
        }

        while (TiledItems.Count > value.Tiles.Count)
        {
            TiledItems.RemoveAt(TiledItems.Count - 1);
        }

        while (TiledItems.Count < value.Tiles.Count)
        {
            TiledItems.Add(NewViewportTile());
        }
    }

    /// <summary>The tree the viewport panel lays its tiles out in, a fresh one of the layout picked.</summary>
    [Bindable] private TileRegion _viewportLayout;

    /// <summary>The viewport panel's tiles, in the order it lays them out; a drag moves one to another place.</summary>
    public ObservableCollection<ColorRect> TiledItems { get; } =
        new(Enumerable.Range(0, 4).Select(i => new ColorRect { Number = i + 1, Color = Palette[i] }));

    /// <summary>Gives a tile split off another a tile of its own, at the index the panel asks for.</summary>
    [Command] private void SplitTile(object at) => TiledItems.Insert(Math.Clamp((int)at, 0, TiledItems.Count), NewViewportTile());

    /// <summary>Lets go of the tile a border was dragged all the way over; the last one stays.</summary>
    [Command]
    private void MergeTile(object item)
    {
        if (TiledItems.Count > 1 && item is ColorRect tile)
        {
            TiledItems.Remove(tile);
        }
    }

    private ColorRect NewViewportTile()
    {
        var number = TiledItems.Count == 0 ? 1 : TiledItems.Max(t => t.Number) + 1;
        return new ColorRect { Number = number, Color = Palette[(number - 1) % Palette.Length] };
    }

    /// <summary>Moves the viewport tile dropped to where it was dropped.</summary>
    [Command] private void DropViewportTile(object arg) => MoveTile(arg, TiledItems);

    /// <summary>Moves the gallery tile dropped to where it was dropped.</summary>
    [Command] private void DropGalleryTile(object arg) => MoveTile(arg, GalleryTiles);

    /// <summary>Moves the featured layout's tile dropped to where it was dropped.</summary>
    [Command] private void DropFeaturedTile(object arg) => MoveTile(arg, FeaturedTiles);

    private static void MoveTile<T>(object arg, ObservableCollection<T> tiles) where T : class
    {
        if (arg is not DragDropEventArgs e || e.Data?.Get<T>() is not { } tile || tiles.IndexOf(tile) is not (>= 0 and var from))
        {
            return;
        }

        var to = e.InsertBefore is T before && tiles.IndexOf(before) is >= 0 and var at ? at : tiles.Count;
        if (to > from)
        {
            to--;
        }

        if (to != from)
        {
            tiles.Move(from, to);
        }
    }

    /// <summary>Whether the first tile fills the panel alone.</summary>
    [Bindable, Affects(nameof(MaximizedTile))] private bool _maximizeFirstTile;

    /// <summary>The tile filling the panel alone, or null.</summary>
    public ColorRect MaximizedTile => MaximizeFirstTile ? TiledItems[0] : null;

    /// <summary>Gives every tile of the viewport layout an even share again.</summary>
    [Command] private void ShareTilesEvenly() => ViewportLayout?.ShareEvenly();

    /// <summary>The gallery's flow, its limits set from the stand.</summary>
    public TileFlow GalleryLayout { get; } = new();

    /// <summary>The gallery's tiles.</summary>
    public ObservableCollection<ShowcaseTile> GalleryTiles { get; } = [];

    /// <summary>How many tiles the gallery holds.</summary>
    [Bindable] private int _galleryCount = 12;

    partial void OnGalleryCountChanged(int value) => Fill(GalleryTiles, value);

    /// <summary>The gallery's most columns; 0 sets no limit.</summary>
    [Bindable] private int _galleryMaxColumns;

    partial void OnGalleryMaxColumnsChanged(int value) => GalleryLayout.MaxColumns = value;

    /// <summary>The gallery's most rows; 0 sets no limit.</summary>
    [Bindable] private int _galleryMaxRows;

    partial void OnGalleryMaxRowsChanged(int value) => GalleryLayout.MaxRows = value;

    /// <summary>The proportion of the gallery's tiles.</summary>
    [Bindable] private TileAspect _galleryAspect;

    partial void OnGalleryAspectChanged(TileAspect value) => GalleryLayout.AspectRatio = RatioOf(value);

    /// <summary>Where the gallery's short last row stands.</summary>
    [Bindable] private HorizontalAlignment _galleryLastRow = HorizontalAlignment.Left;

    partial void OnGalleryLastRowChanged(HorizontalAlignment value) => GalleryLayout.LastRowAlignment = value;

    /// <summary>The featured layout, made again when its side or share changes.</summary>
    [Bindable] private TileRegion _featuredLayout;

    /// <summary>The featured layout's tiles; a tile's star features it, a drag moves one to another place.</summary>
    public ObservableCollection<ShowcaseTile> FeaturedTiles { get; } = [];

    private void Feature(ShowcaseTile tile)
    {
        foreach (var each in FeaturedTiles)
        {
            each.IsFeatured = ReferenceEquals(each, tile);
        }
    }

    /// <summary>How many tiles the featured layout holds.</summary>
    [Bindable] private int _featuredCount = 8;

    partial void OnFeaturedCountChanged(int value) => Fill(FeaturedTiles, value);

    /// <summary>Where the strip stands beside the featured tile.</summary>
    [Bindable] private Dock _featuredSide = Dock.Bottom;

    partial void OnFeaturedSideChanged(Dock value) => ShowFeatured();

    /// <summary>The featured tile's share of the panel, in percent.</summary>
    [Bindable] private double _featuredShare = 80;

    partial void OnFeaturedShareChanged(double value) => ShowFeatured();

    /// <summary>The most tiles in the strip; 0 sets no limit.</summary>
    [Bindable] private int _featuredStripLimit;

    partial void OnFeaturedStripLimitChanged(int value) => ShowFeatured();

    /// <summary>The proportion of the strip's tiles.</summary>
    [Bindable] private TileAspect _featuredAspect;

    partial void OnFeaturedAspectChanged(TileAspect value) => ShowFeatured();

    private void ShowFeatured()
    {
        var across = FeaturedSide is Dock.Top or Dock.Bottom;
        var strip = new TileFlow
        {
            MaxRows = across ? 1 : FeaturedStripLimit,
            MaxColumns = across ? FeaturedStripLimit : 1,
            AspectRatio = RatioOf(FeaturedAspect),
            LastRowAlignment = HorizontalAlignment.Center
        };
        var featured = new Tile(TileLayouts.FeaturedTile);
        var stripFirst = FeaturedSide is Dock.Top or Dock.Left;
        FeaturedLayout = new TileSplit(across ? Orientation.Vertical : Orientation.Horizontal,
            stripFirst ? strip : featured, stripFirst ? featured : strip)
        {
            Shares = stripFirst ? [100 - FeaturedShare, FeaturedShare] : [FeaturedShare, 100 - FeaturedShare]
        };
    }

    private void Fill(ObservableCollection<ShowcaseTile> tiles, int count)
    {
        while (tiles.Count > count)
        {
            tiles.RemoveAt(tiles.Count - 1);
        }

        while (tiles.Count < count)
        {
            var number = tiles.Count + 1;
            tiles.Add(new ShowcaseTile(number, Palette[(number - 1) % Palette.Length], Feature));
        }
    }

    private static double RatioOf(TileAspect aspect) => aspect switch
    {
        TileAspect.Wide => 16.0 / 9,
        TileAspect.Classic => 4.0 / 3,
        TileAspect.Square => 1,
        _ => 0
    };

    // ── THE SKELETON STAND ─────────────────────────────────────────────────────────────────────────────────────────
    // Nothing but the loading cards: a list that never binds an item, shown and hidden by a switch, with the garland's
    // numbers on sliders.

    /// <summary>How many slots the skeleton list has - enough to scroll through.</summary>
    public int SkeletonSlots => 60000;

    /// <summary>What the skeleton list holds: placeholders it never binds while the cards are on, nothing while off.</summary>
    [Bindable] private object _skeletonSource;

    /// <summary>On: every slot of the list shows a skeleton card. Off: the list is empty.</summary>
    [Bindable] private bool _showSkeletons;

    partial void OnShowSkeletonsChanged(bool value) =>
        SkeletonSource = value ? Enumerable.Range(0, SkeletonSlots).ToList() : null;

    /// <summary>How far behind the card before it each card pulses, in milliseconds. 0 pulses them all together.</summary>
    [Bindable] private double _skeletonStaggerMs = 80;

    partial void OnSkeletonStaggerMsChanged(double value) => RaisePropertyChanged(nameof(SkeletonStagger));

    /// <summary>The stagger as the cards take it.</summary>
    public TimeSpan SkeletonStagger => TimeSpan.FromMilliseconds(_skeletonStaggerMs);

    /// <summary>The fill's opacity at the low end of the pulse.</summary>
    [Bindable] private double _pulseMin = 0.05;

    /// <summary>The fill's opacity at the high end of the pulse.</summary>
    [Bindable] private double _pulseMax = 0.15;

    /// <summary>Seconds from the low end to the high one; the way back takes as long again.</summary>
    [Bindable] private double _pulseSeconds = 1.2;

    partial void OnPulseSecondsChanged(double value) => RaisePropertyChanged(nameof(PulseDuration));

    /// <summary>The half-cycle as the pulse takes it.</summary>
    public TimeSpan PulseDuration => TimeSpan.FromSeconds(_pulseSeconds);

    /// <summary>The skeleton list's cell, square. Opens at the smallest, which puts the most cards on screen.</summary>
    [Bindable] private double _skeletonCell = 24;

    // ── VIRTUALIZED, OR PAGED - the same 60 000 tiles either way ───────────────────────────────────────────────────
    // Opens virtualized; the switch pages the same data for comparison.

    /// <summary>The paged view of the same tiles. Five hundred to a page: enough that a page is still a screenful of
    /// work, few enough that the difference from 60 000 is the point.</summary>
    public CollectionView Paged { get; }

    /// <summary>The page sizes THIS tab offers, and they are nothing like a list's. Ten tiles is not a page, it is a
    /// row; the interesting range here starts where a page is a real amount of layout and ends where it is most of the
    /// sixty thousand - which is the comparison the tab is for. The control's own 10/25/50/100 would put every choice
    /// below the point at which either mechanism is under any strain.</summary>
    public int[] PageSteps { get; } = [100, 250, 500, 1000, 2500, 5000];

    /// <summary>What the grid is actually showing - the whole collection, or one page of it.</summary>
    [Bindable] private object _tileSource;

    /// <summary>Off: the grid holds all 60 000 and virtualizes. On: it holds one page and does not have to.</summary>
    [Bindable] private bool _isPaged;

    partial void OnIsPagedChanged(bool value) => TileSource = value ? Paged : (object)Rectangles;

    private static readonly string[] Palette =
        ["#3B82F6", "#22C55E", "#F59E0B", "#EF4444", "#8B5CF6", "#14B8A6", "#EC4899", "#EAB308"];

    public ObservableCollection<ColorRect> Rectangles { get; }

    // The selected tile - bound two-way to ListBox.SelectedItem so the selection is state ON the view-model and survives a
    // tab switch (the view is recreated, this view-model persists), same idea as the tree's node-side selection.
    [Bindable] private ColorRect _selectedRect;

    /// <summary>The smallest cell the sliders offer, and the size the tab OPENS at. The smallest cell is the heaviest
    /// one - it is what fills a window with the most tiles - so it is the configuration a measurement wants, and having
    /// to drag two sliders to reach it makes every reading start from somewhere else. The sliders take their Minimum
    /// from here rather than restating it: the same number twice, in markup and in code, is how the two drift.</summary>
    public double MinCell => 24;

    /// <summary>The largest cell the sliders offer.</summary>
    public double MaxCell => 240;

    // Cell width/height are independent so the aspect ratio (width/height) is adjustable - a non-square cell shows a real
    // ellipse (rx != ry) / a stretched rounded rect.
    [Bindable] private double _cellWidth = 24;
    [Bindable] private double _cellHeight = 24;

    // False = rounded rectangles (RectBatch SDF), true = ellipses (EllipseBatch SDF). A DataTrigger in Layout/TilesStandView.auml
    // swaps ItemTemplate between the two AUML templates (LayoutResources) off this flag.
    [Bindable] private bool _showEllipses;

    // Milliseconds one layout pass may spend (re)binding tile containers while scrolling; the rest is deferred to the
    // next pass and shows a skeleton. 0 = no budget, bind the whole window in one pass. Live on this tab because it is a
    // genuine trade with no universally right answer: measured here, 6 ms binds ~357 slots a pass and defers ~4487, so a
    // big window at the minimum cell never catches up - while a small window is better off binding everything at once.
    [Bindable] private double _bindBudgetMs = 6;
}
