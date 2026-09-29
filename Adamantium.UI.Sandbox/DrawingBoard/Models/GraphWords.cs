namespace Adamantium.UI.Sandbox.DrawingBoard.Models;

/// <summary>Every name the page's graph matches on (socket data types, node kinds, socket names), defined once and grouped
/// by meaning, since the same word appears in several roles.</summary>
public static class GraphWords
{
    /// <summary>WHAT FLOWS through a socket. Two sockets join when these agree, and the pin takes its color from
    /// this - see <see cref="GraphSocketKind"/>, which must offer every one of them.</summary>
    public static class Flows
    {
        public const string Color = "Color";

        public const string Number = "Number";

        public const string Vector = "Vector";

        public const string Texture = "Texture";

        public const string Bool = "Bool";
    }

    /// <summary>WHAT A NODE IS. The word the palette, the drop-down and the loader all look the catalog up with.
    /// </summary>
    public static class Kinds
    {
        public const string Color = "Color";

        public const string Number = "Number";

        public const string Mix = "Mix";

        public const string Add = "Add";

        public const string Scale = "Scale";

        public const string Gray = "Gray";

        public const string Merge = "Merge";

        public const string Output = "Output";
    }

    /// <summary>WHAT A SOCKET IS CALLED. A saved wire is written down as the two names it joins, so these are addresses
    /// as well as labels.</summary>
    public static class Sockets
    {
        public const string Out = "Out";

        public const string A = "A";

        public const string B = "B";

        public const string Amount = "Amount";

        public const string By = "By";

        public const string Color = "Color";

        public const string Colors = "Colors";
    }

    /// <summary>WHICH SIDE of a node - what the plus on an inspector section hands over, and the prefix a socket made
    /// there is named with. This word crosses from markup into code, which is the one seam where a typo cannot be
    /// caught by a compiler at all: mistyped, the plus on the outputs quietly adds an input.</summary>
    public static class Sides
    {
        public const string In = "In";

        public const string Out = "Out";
    }

    /// <summary>WHICH SECTION of the palette a kind stands in.</summary>
    public static class Groups
    {
        public const string Source = "Source";

        public const string Blend = "Blend";

        public const string Result = "Result";
    }
}
