using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>The buffers a <see cref="CurveTileTree"/> is made on, kept from one tree to the next by whoever makes many -
/// a page of paths prepared, one set to a thread - and let go with them: what one tree leaves in them is not read by the
/// next.</summary>
internal sealed class CurveTileBuffers
{
    private double[] _coordinates = [];
    private int[] _quarterLists = [];

    /// <summary>Every tile's list, one after another, in the order the tiles are made.</summary>
    public List<int> Entries { get; } = [];

    public List<double> Lefts { get; } = [];

    public List<double> Tops { get; } = [];

    public List<int> Levels { get; } = [];

    public List<int> ListStarts { get; } = [];

    /// <summary>Two to a tile.</summary>
    public List<float> Points { get; } = [];

    public List<int> Windings { get; } = [];

    public List<int> FirstChildren { get; } = [];

    /// <summary>Room for the four lists a tile cut is weighed by, each up to <paramref name="pieces"/> long.</summary>
    public int[] QuarterLists(int pieces) =>
        _quarterLists.Length >= pieces * 4 ? _quarterLists : _quarterLists = new int[pieces * 4];

    /// <summary>The pieces' coordinates relative to <paramref name="corner"/>, rounded to float as the shader reads
    /// them, six to a piece; longer than that when the buffer was made for more.</summary>
    public double[] CoordinatesOf(QuadraticCurve[] pieces, Vector2<double> corner)
    {
        const int words = CurveTileTree.WordsPerPiece;
        if (_coordinates.Length < pieces.Length * words)
        {
            _coordinates = new double[pieces.Length * words];
        }

        for (var piece = 0; piece < pieces.Length; piece++)
        {
            var at = piece * words;
            _coordinates[at] = (float)(pieces[piece].Start.X - corner.X);
            _coordinates[at + 1] = (float)(pieces[piece].Start.Y - corner.Y);
            _coordinates[at + 2] = (float)(pieces[piece].Control.X - corner.X);
            _coordinates[at + 3] = (float)(pieces[piece].Control.Y - corner.Y);
            _coordinates[at + 4] = (float)(pieces[piece].End.X - corner.X);
            _coordinates[at + 5] = (float)(pieces[piece].End.Y - corner.Y);
        }

        return _coordinates;
    }

    /// <summary>Empties every list, keeping its room.</summary>
    public void Clear()
    {
        Entries.Clear();
        Lefts.Clear();
        Tops.Clear();
        Levels.Clear();
        ListStarts.Clear();
        Points.Clear();
        Windings.Clear();
        FirstChildren.Clear();
    }
}
