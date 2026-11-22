using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Pdf.Document;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>How long the PDFs in the folder <c>ADAM_PDF_TIMINGS</c> names take to be opened and their pages written into
/// drawings, and how much that allocates: the median of five runs after one to warm up, each on the document opened
/// anew - its objects and fonts not yet read, as a viewer meets them. A measure, not a check - run by hand in Release;
/// the figures go to a file in the temporary folder.</summary>
[Explicit("Measures the PDFs of a folder of one's own")]
public class PdfTimings
{
    private const int Runs = 5;
    private const int ScrolledPages = 20;

    [Test]
    public void Measure()
    {
#if DEBUG
        Assert.Ignore("Timings mean something in Release only.");
#endif
        var folder = Environment.GetEnvironmentVariable("ADAM_PDF_TIMINGS");
        Assume.That(folder, Is.Not.Null.And.Not.Empty, "ADAM_PDF_TIMINGS names the folder");
        var report = new StringBuilder();
        report.AppendLine("file  pages  open ms  first page ms  MB  page of scroll ms  MB");
        foreach (var file in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories).Order())
        {
            var name = Path.GetRelativePath(folder, file);
            try
            {
                report.AppendLine(Measure(name, File.ReadAllBytes(file)));
            }
            catch (Exception exception)
            {
                report.AppendLine($"{name}  not measured: {exception.Message}");
            }
        }

        var output = Path.Combine(Path.GetTempPath(), "pdf-timings.txt");
        File.WriteAllText(output, report.ToString());
        TestContext.Out.WriteLine(report.ToString());
        TestContext.Out.WriteLine(output);
    }

    private static string Measure(string name, byte[] bytes)
    {
        var pageCount = PdfDocument.Open(bytes).PageCount;
        var open = MedianOf(() => PdfDocument.Open(bytes).PageCount);
        var first = MedianOf(() =>
        {
            var document = PdfDocument.Open(bytes);
            PdfPageDrawing.Build(document, document.Pages[0]);
        });
        var scrolled = Math.Min(ScrolledPages, pageCount);
        var scroll = MedianOf(() =>
        {
            var document = PdfDocument.Open(bytes);
            for (var index = 0; index < scrolled; index++)
            {
                PdfPageDrawing.Build(document, document.Pages[index]);
            }
        });
        return $"{name}  {pageCount}  {open.Ms:F1}  {first.Ms:F1}  {first.Megabytes:F1}  {scroll.Ms / scrolled:F1}  " +
               $"{scroll.Megabytes / scrolled:F1}";
    }

    private static Measured MedianOf(Action action)
    {
        action();
        var times = new List<double>(Runs);
        var allocated = new List<long>(Runs);
        for (var run = 0; run < Runs; run++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var start = Stopwatch.GetTimestamp();
            action();
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
        }

        times.Sort();
        allocated.Sort();
        return new Measured(times[Runs / 2], allocated[Runs / 2] / 1048576.0);
    }

    private static Measured MedianOf<TResult>(Func<TResult> action) => MedianOf(() => { action(); });

    private readonly record struct Measured(double Ms, double Megabytes);
}
