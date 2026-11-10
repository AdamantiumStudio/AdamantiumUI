using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Adamantium.Pdf.Document;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Writes a one-page PDF for a test by the format's rules: the catalog (1), the page tree (2), the page (3),
/// its contents (4), then the test's own objects from 5 on, at the offsets the cross-reference table gives.</summary>
internal sealed class TestPdf
{
    private readonly List<byte[]> _objects = [];

    public string MediaBox { get; set; } = "[0 0 200 100]";

    public string Resources { get; set; } = "<< >>";

    public string PageExtra { get; set; } = "";

    public string CatalogExtra { get; set; } = "";

    /// <summary>Adds an object; its number is returned - 5 for the first.</summary>
    public int Add(string body) => Add(Latin1(body));

    public int Add(byte[] body)
    {
        _objects.Add(body);
        return 4 + _objects.Count;
    }

    /// <summary>Adds a stream with its length worked out.</summary>
    public int AddStream(string dictionary, string data) => Add(Stream(dictionary, Latin1(data)));

    public int AddStream(string dictionary, byte[] data) => Add(Stream(dictionary, data));

    public PdfDocument Open(string contents) => PdfDocument.Open(Build(contents));

    public byte[] Build(string contents)
    {
        var all = new List<byte[]>
        {
            Latin1("<< /Type /Catalog /Pages 2 0 R " + CatalogExtra + " >>"),
            Latin1("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Latin1($"<< /Type /Page /Parent 2 0 R /MediaBox {MediaBox} /Resources {Resources} /Contents 4 0 R {PageExtra} >>"),
            Stream("", Latin1(contents)),
        };
        all.AddRange(_objects);
        using var output = new MemoryStream();
        output.Write(Latin1("%PDF-1.7\n%âãÏÓ\n"));
        var offsets = new List<long>();
        for (var i = 0; i < all.Count; i++)
        {
            offsets.Add(output.Position);
            output.Write(Latin1((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
            output.Write(all[i]);
            output.Write(Latin1("\nendobj\n"));
        }

        var xref = output.Position;
        var table = new StringBuilder();
        table.Append("xref\n0 ").Append(all.Count + 1).Append('\n').Append("0000000000 65535 f\r\n");
        foreach (var offset in offsets)
        {
            table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n\r\n");
        }

        table.Append("trailer\n<< /Size ").Append(all.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref)
            .Append("\n%%EOF\n");
        output.Write(Latin1(table.ToString()));
        return output.ToArray();
    }

    public static byte[] Stream(string dictionary, byte[] data)
    {
        using var output = new MemoryStream();
        output.Write(Latin1("<< " + dictionary + " /Length " + data.Length.ToString(CultureInfo.InvariantCulture) +
                            " >>\nstream\n"));
        output.Write(data);
        output.Write(Latin1("\nendstream"));
        return output.ToArray();
    }

    public static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
