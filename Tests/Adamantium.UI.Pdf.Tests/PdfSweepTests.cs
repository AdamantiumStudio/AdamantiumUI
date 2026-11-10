using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Every page of every PDF in the folder <c>ADAM_PDF_SWEEP</c> names is built and replayed: none may throw. What
/// the pages could not draw, and how long they took, is written out. Run by hand, on files of one's own.</summary>
[Explicit("Reads the PDFs of a folder of one's own")]
public class PdfSweepTests
{
    [Test]
    public void EveryPageOfTheSweptFilesIsDrawn()
    {
        var folder = Environment.GetEnvironmentVariable("ADAM_PDF_SWEEP");
        Assume.That(folder, Is.Not.Null.And.Not.Empty, "ADAM_PDF_SWEEP names the folder");
        var problems = new Dictionary<string, int>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
        {
            PdfDocument document;
            try
            {
                document = PdfDocument.Open(file);
            }
            catch (Exception exception)
            {
                TestContext.Out.WriteLine($"{Path.GetFileName(file)}: not opened ({exception.Message})");
                continue;
            }

            var watch = Stopwatch.StartNew();
            var slowest = (Page: 0, Ms: 0.0);
            var steps = 0;
            for (var i = 0; i < document.PageCount; i++)
            {
                var start = watch.Elapsed.TotalMilliseconds;
                var page = PdfPageDrawing.Build(document, document.Pages[i]);
                var session = new RecordingSession();
                page.Render(session, Matrix4x4<float>.Identity, null);
                Assert.That(session.Depth, Is.Zero, $"{file} page {i + 1}: every group closes");
                steps += session.Calls.Count;
                foreach (var problem in page.Problems)
                {
                    problems[problem] = problems.GetValueOrDefault(problem) + 1;
                }

                var took = watch.Elapsed.TotalMilliseconds - start;
                if (took > slowest.Ms)
                {
                    slowest = (i + 1, took);
                }
            }

            TestContext.Out.WriteLine($"{Path.GetFileName(file)}: {document.PageCount} pages, {steps} draws, " +
                                      $"{watch.Elapsed.TotalMilliseconds:F0} ms, slowest page {slowest.Page} " +
                                      $"{slowest.Ms:F0} ms");
        }

        foreach (var (problem, count) in problems.OrderByDescending(p => p.Value).Take(40))
        {
            TestContext.Out.WriteLine($"{count,6} x {problem}");
        }
    }
}
