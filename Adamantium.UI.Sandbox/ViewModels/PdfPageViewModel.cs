using System;
using System.Diagnostics;
using System.IO;
using Adamantium.Core.Collections;
using Adamantium.MVVM;
using Adamantium.Pdf;
using Adamantium.Pdf.Document;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Pdf;
using Adamantium.UI.Sandbox.Models;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>PDF page tab: a page of a PDF drawn as a drawing - the sample written here, or a file of one's own - with
/// what it could not draw listed under it.</summary>
[ViewModel]
public partial class PdfPageViewModel : TabPageViewModel
{
    private PdfDocument _document;
    private string _name = "";

    public PdfPageViewModel() : base("PdfPage")
    {
        Load(PdfDocument.Open(PdfSampleDocument.Build()), "sample");
    }

    [Bindable] private string _filePath = "";
    [Bindable] private DrawingImage _page;
    [Bindable] private double _pageWidth;
    [Bindable] private double _pageHeight;
    [Bindable] private int _pageNumber;
    [Bindable] private int _pageCount;
    [Bindable] private string _status = "";

    /// <summary>What the page shown could not draw.</summary>
    public TrackingCollection<string> Problems { get; } = [];

    [Command]
    private void Open()
    {
        var path = FilePath?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Status = "✕ " + path;
            return;
        }

        try
        {
            Load(PdfDocument.Open(path), Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is PdfException or IOException or UnauthorizedAccessException)
        {
            Status = exception.Message;
        }
    }

    [Command]
    private void Sample() => Load(PdfDocument.Open(PdfSampleDocument.Build()), "sample");

    [Bindable] private string _goTo = "";

    [Command]
    private void Go()
    {
        if (int.TryParse(GoTo, out var number))
        {
            Show(number);
        }
    }

    [Command]
    private void Previous() => Show(PageNumber - 1);

    [Command]
    private void Next() => Show(PageNumber + 1);

    private void Load(PdfDocument document, string name)
    {
        _document = document;
        PageCount = document.PageCount;
        Show(1, name);
    }

    private void Show(int number, string name = null)
    {
        if (_document == null || number < 1 || number > _document.PageCount)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        var drawing = PdfPageDrawing.Build(_document, _document.Pages[number - 1]);
        PageNumber = number;
        PageWidth = drawing.Bounds.Width;
        PageHeight = drawing.Bounds.Height;
        Page = new DrawingImage { Drawing = drawing };
        Problems.Clear();
        foreach (var problem in drawing.Problems)
        {
            Problems.Add(problem);
        }

        if (name != null)
        {
            _name = name;
        }

        Status = $"{_name} · {watch.Elapsed.TotalMilliseconds:F0} ms";
    }
}
