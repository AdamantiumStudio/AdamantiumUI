using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Adamantium.UI.Sandbox.Models;

/// <summary>A one-page PDF written here, by the format's rules, to show what a page can hold: paths and pens, clips,
/// text set in a font the file does not carry, shadings, a tiling pattern, transparency and blend modes, an image.
/// </summary>
public static class PdfSampleDocument
{
    private const string Contents = """
        q 0.97 0.97 0.95 rg 0 0 612 792 re f Q
        BT /F1 30 Tf 0.12 0.16 0.3 rg 48 730 Td (Adamantium PDF) Tj ET
        BT /F2 12 Tf 0.35 0.38 0.45 rg 48 708 Td (Paths, clips, text, shadings, patterns, transparency) Tj ET

        q 0.85 0.2 0.25 rg 48 560 120 110 re f
          4 w 0.1 0.1 0.1 RG 1 J 1 j [12 6] 0 d 190 560 120 110 re S
          [] 0 d 0.2 0.5 0.9 rg 0 0.2 0.4 RG 2 w
          390 670 m 410 615 l 468 615 l 421 581 l 439 526 l 390 560 l 341 526 l 359 581 l 312 615 l 370 615 l h B*
        Q

        q 48 380 250 140 re W n /Sh1 sh Q
        q 1 0 0 1 430 450 cm 0 0 m 0 0 70 70 re W n /Sh2 sh Q
        q 330 380 50 140 re W n /Sh2 sh Q
        q 480 520 110 160 re W n /Sh3 sh Q
        /Sh4 sh
        /Sh5 sh

        q /Pattern cs /P1 scn 48 220 250 130 re f Q
        q 0 0 0 RG 1 w 48 220 250 130 re S Q

        q /GA gs 1 0.6 0 rg 380 300 m 380 355 425 400 480 400 c 535 400 580 355 580 300 c 580 245 535 200 480 200 c
          425 200 380 245 380 300 c f
          0 0.6 1 rg 330 260 m 330 315 375 360 430 360 c 485 360 530 315 530 260 c 530 205 485 160 430 160 c
          375 160 330 205 330 260 c f Q
        q /GM gs 0.9 0.9 0.1 rg 400 150 150 60 re f Q

        q BT /F1 60 Tf 7 Tr 48 90 Td (CLIPPED) Tj ET /Sh1 sh Q

        q 0.866 0.5 -0.5 0.866 360 60 cm BT /F2 16 Tf 0.2 0.2 0.2 rg 0 0 Td (Turned text, 30 degrees) Tj ET Q
        q 60 0 20 40 520 40 cm BI /W 4 /H 4 /CS /RGB /BPC 8 ID
        """;

    // A 4x4 RGB image: a row each of red, green, blue and a gray ramp.
    private static readonly byte[] ImageBytes =
    [
        255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0,
        0, 200, 0, 0, 200, 0, 0, 200, 0, 0, 200, 0,
        0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255,
        0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255,
    ];

    public static byte[] Build()
    {
        // ID is followed by one white-space character, then the image's bytes.
        var contents = Concat(Latin1(Contents + "\n"), ImageBytes, Latin1("\nEI Q\n"));
        var objects = new List<byte[]>
        {
            Latin1("<< /Type /Catalog /Pages 2 0 R >>"),
            Latin1("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Latin1("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << " +
                   "/Font << /F1 5 0 R /F2 6 0 R >> /Shading << /Sh1 7 0 R /Sh2 8 0 R /Sh3 12 0 R /Sh4 13 0 R /Sh5 14 0 R >> " +
                   "/Pattern << /P1 10 0 R >> " +
                   "/ExtGState << /GA << /ca 0.6 >> /GM << /BM /Multiply >> >> >> >>"),
            Stream("", contents),
            Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
            Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Latin1("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [48 0 298 0] /Function 9 0 R " +
                   "/Extend [true true] >>"),
            Latin1("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [20 50 0 35 35 35] /Function 11 0 R " +
                   "/Extend [false true] >>"),
            Latin1("<< /FunctionType 2 /Domain [0 1] /C0 [0.1 0.3 0.9] /C1 [0.95 0.4 0.6] /N 1 >>"),
            Stream("/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 /Resources << >>",
                Latin1("0.9 0.9 0.9 rg 0 0 20 20 re f 0.2 0.6 0.4 rg 0 0 10 10 re f 10 10 10 10 re f")),
            Latin1("<< /FunctionType 2 /Domain [0 1] /C0 [1 1 0.8] /C1 [0.8 0.2 0.1] /N 1 >>"),
            // Two circles apart: a cone of circles from a small one low down to a large one higher up.
            Latin1("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [510 545 8 545 640 36] /Function 9 0 R " +
                   "/Extend [true true] >>"),
            Stream("/ShadingType 6 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 " +
                   "/Decode [0 65535 0 65535 0 1 0 1 0 1]", CoonsPatch()),
            Stream("/ShadingType 6 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 " +
                   "/Decode [0 65535 0 65535 0 1 0 1 0 1]", FoldedPatch()),
        };

        using var output = new MemoryStream();
        output.Write(Latin1("%PDF-1.7\n%âãÏÓ\n"));
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            output.Write(Latin1((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
            output.Write(objects[i]);
            output.Write(Latin1("\nendobj\n"));
        }

        var xref = output.Position;
        var table = new StringBuilder();
        table.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f\r\n");
        foreach (var offset in offsets)
        {
            table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n\r\n");
        }

        table.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");
        output.Write(Latin1(table.ToString()));
        return output.ToArray();
    }

    private static byte[] CoonsPatch() => EncodePatch(
        [
            200, 160, 215, 185, 205, 205, 220, 230,
            250, 245, 280, 225, 310, 235,
            300, 210, 310, 185, 300, 160,
            270, 145, 235, 170,
        ],
        [255, 80, 0, 255, 220, 0, 0, 160, 255, 160, 0, 200]);

    private static byte[] FoldedPatch() => EncodePatch(
        [
            420, 700, 420, 720, 420, 740, 420, 760,
            560, 760, 380, 760, 450, 760,
            450, 740, 450, 720, 450, 700,
            380, 700, 560, 700,
        ],
        [230, 40, 60, 250, 200, 40, 40, 170, 90, 120, 60, 220]);

    private static byte[] EncodePatch(int[] coordinates, byte[] colors)
    {
        var data = new List<byte> { 0 };
        foreach (var coordinate in coordinates)
        {
            data.AddRange([(byte)(coordinate >> 8), (byte)coordinate]);
        }

        data.AddRange(colors);
        return data.ToArray();
    }

    private static byte[] Stream(string dictionary, byte[] data) =>
        Concat(Latin1("<< " + dictionary + " /Length " + data.Length.ToString(CultureInfo.InvariantCulture) +
                      " >>\nstream\n"), data, Latin1("\nendstream"));

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Concat(params byte[][] parts)
    {
        using var output = new MemoryStream();
        foreach (var part in parts)
        {
            output.Write(part);
        }

        return output.ToArray();
    }
}
