using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Controls.Printing;

/// <summary>One sheet as it will be printed: the paper, fitted into the control's room, and what is printed on it drawn
/// by the same layout that goes to the printer or the file.</summary>
public class PrintPreview : MeasurableUIComponent
{
    public static readonly AdamantiumProperty SheetsProperty = AdamantiumProperty.Register(nameof(Sheets),
        typeof(PrintSheets), typeof(PrintPreview), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty SheetProperty = AdamantiumProperty.Register(nameof(Sheet),
        typeof(int), typeof(PrintPreview), new PropertyMetadata(0, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty PaperBrushProperty = AdamantiumProperty.Register(nameof(PaperBrush),
        typeof(Brush), typeof(PrintPreview), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty ShadowBrushProperty = AdamantiumProperty.Register(nameof(ShadowBrush),
        typeof(Brush), typeof(PrintPreview), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    /// <summary>The sheets shown - laid out already.</summary>
    public PrintSheets Sheets
    {
        get => GetValue<PrintSheets>(SheetsProperty);
        set => SetValue(SheetsProperty, value);
    }

    /// <summary>Which sheet, from 0.</summary>
    public int Sheet
    {
        get => GetValue<int>(SheetProperty);
        set => SetValue(SheetProperty, value);
    }

    /// <summary>What the paper is drawn in - white, as paper is, in any theme.</summary>
    public Brush PaperBrush
    {
        get => GetValue<Brush>(PaperBrushProperty);
        set => SetValue(PaperBrushProperty, value);
    }

    /// <summary>What the paper's edge and shadow are drawn in.</summary>
    public Brush ShadowBrush
    {
        get => GetValue<Brush>(ShadowBrushProperty);
        set => SetValue(ShadowBrushProperty, value);
    }

    /// <summary>The room left round the paper, in DIPs.</summary>
    private const double Room = 12;

    protected override AutomationPeer OnCreateAutomationPeer() => new PrintPreviewAutomationPeer(this);

    protected override void OnRender(IDrawingContext context)
    {
        base.OnRender(context);
        Draw(context.ForControl(this));
    }

    /// <summary>Where the paper is drawn in the control, fitted; empty when there is nothing to show.</summary>
    internal Rect Paper
    {
        get
        {
            var size = RenderSize;
            if (Sheets is not { } sheets || size.Width <= Room * 2 || size.Height <= Room * 2)
            {
                return default;
            }

            var sheet = sheets.Sheet.Size;
            var scale = Math.Min((size.Width - Room * 2) / sheet.Width, (size.Height - Room * 2) / sheet.Height);
            var width = sheet.Width * scale;
            var height = sheet.Height * scale;
            return new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
        }
    }

    /// <summary>Draws the sheet into a session: the paper, its shadow, and the sheet's pages cut to it.</summary>
    internal void Draw(IDrawingSession session)
    {
        var paper = Paper;
        if (session == null || Sheets is not { } sheets || paper.Width <= 0 || paper.Height <= 0)
        {
            return;
        }

        if (ShadowBrush is { } shadow)
        {
            session.DrawRectangle(shadow, new Rect(paper.X + 2, paper.Y + 3, paper.Width, paper.Height));
        }

        session.DrawRectangle(PaperBrush ?? new SolidColorBrush(Colors.White), paper);
        var scale = paper.Width / sheets.Sheet.Size.Width;
        session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(paper) });
        sheets.DrawPage(Math.Max(0, Sheet), new TransformedDrawingSession(session,
            Matrix4x4.CreateScale((float)scale, (float)scale, 1f) *
            Matrix4x4.CreateTranslation((float)paper.X, (float)paper.Y, 0f)));
        session.PopGroup();
    }
}
