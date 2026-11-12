namespace Adamantium.UITests.Rendering;

/// <summary>What getting a scene ready on the CPU costs each way.</summary>
/// <param name="MeshMilliseconds">Cutting its geometries into triangles.</param>
/// <param name="CurvesMilliseconds">Cutting their curves into pieces and tiles and packing them for the shader.</param>
/// <param name="Pieces">How many pieces the curves were cut into.</param>
/// <param name="Words">How many 32-bit words the packed curves take.</param>
internal readonly record struct CurvePrepCost(double MeshMilliseconds, double CurvesMilliseconds, int Pieces, long Words);
