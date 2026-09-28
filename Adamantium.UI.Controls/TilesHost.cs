using Adamantium.UI.Controls.Generators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>Items host for a 3D flip-tile board: tiles tilt toward the pointer, <see cref="IsFlipped"/> sweeps a flip wave,
/// and each tile reveals its fragment of the shared <see cref="Photo"/>.</summary>
public class TilesHost : ItemsControl
{
    /// <summary>The one photo the whole board reveals; decoded once, its texture shared by every tile.</summary>
    public static readonly AdamantiumProperty PhotoProperty = AdamantiumProperty.Register(nameof(Photo),
        typeof(ImageSource), typeof(TilesHost), new PropertyMetadata(null, OnPhotoChanged));

    /// <summary>How many tiles stand in a row. The board's SHAPE belongs to the board, not to whatever panel happens to
    /// lay it out: a tile's slice of the photo is a function of its item index AND this number, so a control that had to
    /// read the shape back off its panel would answer differently - or not at all - the moment somebody templated a
    /// different one in. The theme's ItemsPanel is handed these; see the TilesHost style sets.</summary>
    public static readonly AdamantiumProperty ColumnsProperty = AdamantiumProperty.Register(nameof(Columns),
        typeof(int), typeof(TilesHost), new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>How many rows the board stands in. Left at zero it follows from <see cref="Columns"/> and the item
    /// count, which is the usual case - a board is authored as "twelve across".</summary>
    public static readonly AdamantiumProperty RowsProperty = AdamantiumProperty.Register(nameof(Rows),
        typeof(int), typeof(TilesHost), new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>The gap between tiles, stated once for the board rather than as a margin on every tile - so a tile is
    /// exactly its cell, which is what lets the photo's lines run straight across the gaps.</summary>
    public static readonly AdamantiumProperty SpacingProperty = AdamantiumProperty.Register(nameof(Spacing),
        typeof(double), typeof(TilesHost), new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>Board state: setting it flips every tile as a diagonal wave (see <see cref="WaveDuration"/>).</summary>
    public static readonly AdamantiumProperty IsFlippedProperty = AdamantiumProperty.Register(nameof(IsFlipped),
        typeof(bool), typeof(TilesHost), new PropertyMetadata(false, OnIsFlippedChanged));

    /// <summary>Tilt clamp in degrees - how far a tile may lean toward the cursor.</summary>
    public static readonly AdamantiumProperty TiltMaxAngleProperty = AdamantiumProperty.Register(nameof(TiltMaxAngle),
        typeof(double), typeof(TilesHost), new PropertyMetadata(34.0));

    /// <summary>How fast the lean grows with the tile's distance from the cursor (degrees per pixel).</summary>
    public static readonly AdamantiumProperty TiltAnglePerPixelProperty = AdamantiumProperty.Register(nameof(TiltAnglePerPixel),
        typeof(double), typeof(TilesHost), new PropertyMetadata(0.045));

    /// <summary>How long the flip wave takes to sweep from the board's first tile to its last (seconds); each tile's
    /// start delay is its diagonal position within the board scaled into this window.</summary>
    public static readonly AdamantiumProperty WaveDurationProperty = AdamantiumProperty.Register(nameof(WaveDuration),
        typeof(double), typeof(TilesHost), new PropertyMetadata(0.6));

    public ImageSource Photo { get => GetValue<ImageSource>(PhotoProperty); set => SetValue(PhotoProperty, value); }
    public int Columns { get => GetValue<int>(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public int Rows { get => GetValue<int>(RowsProperty); set => SetValue(RowsProperty, value); }
    public double Spacing { get => GetValue<double>(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public bool IsFlipped { get => GetValue<bool>(IsFlippedProperty); set => SetValue(IsFlippedProperty, value); }
    public double TiltMaxAngle { get => GetValue<double>(TiltMaxAngleProperty); set => SetValue(TiltMaxAngleProperty, value); }
    public double TiltAnglePerPixel { get => GetValue<double>(TiltAnglePerPixelProperty); set => SetValue(TiltAnglePerPixelProperty, value); }
    public double WaveDuration { get => GetValue<double>(WaveDurationProperty); set => SetValue(WaveDurationProperty, value); }

    private readonly List<FlipTile> _tiles = new();
    private LayoutManager _hookedManager;

    public TilesHost()
    {
        MouseMove += OnHostMouseMove;
        MouseLeave += OnHostMouseLeave;
    }

    // Re-assign fragments after each layout pass so tiles realized while scrolling get their UVs; render-only writes, so
    // no layout loop.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_hookedManager == null)
        {
            _hookedManager = LayoutManager.For(this);
            _hookedManager.LayoutUpdated += OnLayoutUpdated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_hookedManager != null)
        {
            _hookedManager.LayoutUpdated -= OnLayoutUpdated;
            _hookedManager = null;
        }
    }

    private void OnLayoutUpdated(object sender, EventArgs e) => AssignFragments();

    // --- Photo + UV fragments -------------------------------------------------------------------------------------

    private static void OnPhotoChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is TilesHost host) host.AssignFragments();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        // Tiles have their final bounds now: hand each one the shared photo and its gap-true UV fragment. Repeat
        // arranges re-write the same values (a no-op set) - only a real layout change moves the fragments.
        AssignFragments();
        return size;
    }

    private void AssignFragments()
    {
        CollectTiles();
        if (_tiles.Count == 0) return;
        var photo = Photo;

        // Prefer index-based UVs on a uniform grid, correct under virtualization; the realized-bounds union below is the
        // fallback for other panels.
        if (DescribeGrid() is { } grid)
            AssignFragmentsByIndex(photo, grid);
        else
            AssignFragmentsByUnion(photo);
    }

    // The grid the photo maps over, from Columns/Rows/Spacing and the panel's size, never the panel's type; null when no
    // shape was set.
    private TileGrid? DescribeGrid()
    {
        var count = Items.Count;
        if (count == 0) return null;

        var cols = Columns > 0 ? Columns : Rows > 0 ? (count + Rows - 1) / Rows : 0;
        if (cols <= 0) return null;

        var rows = Rows > 0 ? Rows : (count + cols - 1) / cols;
        var box = ItemsHostPanel?.RenderSize ?? default;
        var tileW = (box.Width - (cols - 1) * Spacing) / cols;
        var tileH = (box.Height - (rows - 1) * Spacing) / rows;
        if (tileW <= 0 || tileH <= 0) return null;

        return new TileGrid(cols, tileW + Spacing, tileH + Spacing, tileW, tileH);
    }

    private readonly record struct TileGrid(int Columns, double CellW, double CellH, double TileW, double TileH);

    // Photo maps over the full grid: cell PITCH (incl. gap) spaces the columns/rows, each tile samples the tile-sized slot
    // inside its cell, so image lines run straight across the inter-tile gaps - exactly the union result, reconstructed
    // from the item index instead of measured bounds (so off-screen/virtualized tiles don't distort it).
    private void AssignFragmentsByIndex(ImageSource photo, TileGrid grid)
    {
        var (cols, cellW, cellH, tileW, tileH) = grid;
        var count = Items.Count;
        var rows = (count + cols - 1) / cols;

        var unionW = (cols - 1) * cellW + tileW;   // grid union: col 0 .. last col
        var unionH = (rows - 1) * cellH + tileH;   // row 0 .. last row
        if (tileW <= 0 || tileH <= 0 || unionW <= 0 || unionH <= 0) { AssignFragmentsByUnion(photo); return; }

        var generator = ItemContainerGenerator;
        foreach (var tile in _tiles)
        {
            var index = IndexOfTile(tile, generator);
            if (index < 0) continue;
            tile.Photo = photo;
            tile.SourceU = (index % cols) * cellW / unionW;
            tile.SourceV = (index / cols) * cellH / unionH;
            tile.SourceUW = tileW / unionW;
            tile.SourceVH = tileH / unionH;
        }
    }

    // Fallback: map the photo over the union of the CURRENTLY-realized tile rects. Correct only when every tile is realized
    // (no item scrolling / a non-uniform panel); with virtualization the index path above is what keeps the photo whole.
    private void AssignFragmentsByUnion(ImageSource photo)
    {
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        var rects = new Rect[_tiles.Count];
        for (var i = 0; i < _tiles.Count; i++)
        {
            rects[i] = RectInHost(_tiles[i]);
            l = Math.Min(l, rects[i].X);
            t = Math.Min(t, rects[i].Y);
            r = Math.Max(r, rects[i].Right);
            b = Math.Max(b, rects[i].Bottom);
        }
        var w = r - l;
        var h = b - t;
        if (w <= 0 || h <= 0) return;

        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            tile.Photo = photo;
            tile.SourceU = (rects[i].X - l) / w;
            tile.SourceV = (rects[i].Y - t) / h;
            tile.SourceUW = rects[i].Width / w;
            tile.SourceVH = rects[i].Height / h;
        }
    }

    // A realized tile's absolute item index: the generator maps its ITEM CONTAINER (the ContentPresenter the tile sits in)
    // to an index; walk up from the tile to the first ancestor the generator knows.
    private static int IndexOfTile(FlipTile tile, ItemContainerGenerator generator)
    {
        for (IUIComponent p = tile; p != null; p = p.VisualParent)
        {
            var index = generator.IndexFromContainer(p);
            if (index >= 0) return index;
        }
        return -1;
    }

    // --- Flip wave ------------------------------------------------------------------------------------------------

    private static void OnIsFlippedChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        // Same Unset-gate trap as FlipTile.OnIsFlippedChanged: the binding's first push arrives with OldValue = Unset,
        // and gating on it swallowed the board's FIRST sweep (state flipped, no wave). Compare effective values.
        var was = e.OldValue is bool oldFlipped && oldFlipped;
        var now = (bool)e.NewValue;
        if (a is TilesHost host && was != now)
            host.FlipWave(now);
    }

    private void FlipWave(bool flipped)
    {
        // Assign photo/UV to every realized tile BEFORE flipping it: a flip is a render-transform (no layout pass), so the
        // LayoutUpdated re-assign hook does NOT fire from a flip - a tile realized since the last layout pass would flip to
        // its back with the default fragment (no texture) until some later layout pass re-assigned it (the "click 2-3 times"
        // symptom). Assigning here (it collects the tiles too) closes that gap. Then flip the same collected set.
        AssignFragments();
        if (_tiles.Count == 0) return;

        // Diagonal wave: a tile's start delay is its (x+y) position across the board normalised into WaveDuration.
        double maxDiag = 0;
        var centres = new Vector2[_tiles.Count];
        for (var i = 0; i < _tiles.Count; i++)
        {
            var rect = RectInHost(_tiles[i]);
            centres[i] = new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            maxDiag = Math.Max(maxDiag, centres[i].X + centres[i].Y);
        }
        var wave = WaveDuration;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            tile.FlipDelay = maxDiag > 0 ? (centres[i].X + centres[i].Y) / maxDiag * wave : 0;
            tile.IsFlipped = flipped;
        }
    }

    // --- Tilt field -----------------------------------------------------------------------------------------------

    private void OnHostMouseMove(object sender, MouseEventArgs e)
    {
        var cursor = e.GetPosition(this);
        var maxAngle = TiltMaxAngle;
        var perPixel = TiltAnglePerPixel;

        CollectTiles();
        foreach (var tile in _tiles)
        {
            var rect = RectInHost(tile);
            var dx = cursor.X - (rect.X + rect.Width / 2);
            var dy = cursor.Y - (rect.Y + rect.Height / 2);
            // CONCAVE dish around the pointer (a satellite antenna, not a bump): every tile's near edge - the one
            // facing the cursor - sinks away from the viewer, so the whole board reads as a bowl centred on the cursor.
            var rotY = Math.Clamp(dx * perPixel, -maxAngle, maxAngle);
            var rotX = Math.Clamp(-dy * perPixel, -maxAngle, maxAngle);
            tile.SetFieldTilt(rotX, rotY);
        }
    }

    private void OnHostMouseLeave(object sender, MouseEventArgs e)
    {
        CollectTiles();
        foreach (var tile in _tiles)
            tile.EaseTiltBack();
    }

    // --- Helpers --------------------------------------------------------------------------------------------------

    private Rect RectInHost(FlipTile tile)
    {
        double x = tile.Bounds.X, y = tile.Bounds.Y;
        for (IUIComponent p = tile.VisualParent; p != null && !ReferenceEquals(p, this); p = p.VisualParent)
        {
            x += p.Bounds.X;
            y += p.Bounds.Y;
        }
        return new Rect(x, y, tile.Bounds.Width, tile.Bounds.Height);
    }

    private void CollectTiles()
    {
        _tiles.Clear();
        Collect(this, _tiles);

        static void Collect(IUIComponent node, List<FlipTile> tiles)
        {
            if (node is FlipTile tile) { tiles.Add(tile); return; }
            foreach (var child in node.VisualChildren)
                Collect(child, tiles);
        }
    }
}
