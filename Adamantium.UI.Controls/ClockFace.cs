using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A clock's face: a plate and what is marked round its edge (<see cref="FaceStyle"/>) - the hours in figures or
/// Roman numerals, a mark for every minute or hour, dots - or labels of its own (<see cref="Labels"/>, and
/// <see cref="InnerLabels"/> on a ring inside), one of them lit (<see cref="ChosenLabel"/>). Drawn to whatever size it is
/// given: the marks and figures grow with it. The face of a <see cref="Clock"/>, and of a <see cref="TimePicker"/>'s
/// dial.</summary>
public class ClockFace : Control
{
    public static readonly AdamantiumProperty FaceStyleProperty = AdamantiumProperty.Register(nameof(FaceStyle),
        typeof(ClockFaceStyle), typeof(ClockFace), new PropertyMetadata(ClockFaceStyle.Classic, OnMarksChanged));

    public static readonly AdamantiumProperty LabelsProperty = AdamantiumProperty.Register(nameof(Labels),
        typeof(IReadOnlyList<string>), typeof(ClockFace), new PropertyMetadata(null, OnMarksChanged));

    public static readonly AdamantiumProperty InnerLabelsProperty = AdamantiumProperty.Register(nameof(InnerLabels),
        typeof(IReadOnlyList<string>), typeof(ClockFace), new PropertyMetadata(null, OnMarksChanged));

    public static readonly AdamantiumProperty ChosenLabelProperty = AdamantiumProperty.Register(nameof(ChosenLabel),
        typeof(int), typeof(ClockFace), new PropertyMetadata(-1, OnChosenChanged));

    /// <summary>How far out the hour figures stand, as a share of the face's radius.</summary>
    public const double LabelRadius = 0.786;

    /// <summary>How far out the inner ring of labels stands, as a share of the face's radius.</summary>
    public const double InnerLabelRadius = 0.518;

    private static readonly string[] Arabic = ["12", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11"];
    private static readonly string[] Roman = ["XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI"];

    private readonly List<TextBlock> _labels = new();
    private readonly List<TextBlock> _innerLabels = new();
    private readonly List<Border> _ticks = new();
    private readonly List<Border> _dots = new();
    private readonly RadialPanel _marks = new();
    private Panel _host;
    private double _side;

    /// <summary>What is marked round the edge.</summary>
    public ClockFaceStyle FaceStyle
    {
        get => GetValue<ClockFaceStyle>(FaceStyleProperty);
        set => SetValue(FaceStyleProperty, value);
    }

    /// <summary>Twelve labels round the edge, from the top clockwise, in place of the hour figures.</summary>
    public IReadOnlyList<string> Labels
    {
        get => GetValue<IReadOnlyList<string>>(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>Twelve labels on a ring inside the first - the afternoon hours of a 24-hour dial; null for none.</summary>
    public IReadOnlyList<string> InnerLabels
    {
        get => GetValue<IReadOnlyList<string>>(InnerLabelsProperty);
        set => SetValue(InnerLabelsProperty, value);
    }

    /// <summary>The label lit: 0 to 11 round the edge, 12 to 23 on the inner ring; -1 for none.</summary>
    public int ChosenLabel
    {
        get => GetValue<int>(ChosenLabelProperty);
        set => SetValue(ChosenLabelProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_Marks") as Panel;
        _host?.Children.Add(_marks);
        Build();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        _host?.Children.Remove(_marks);
        _host = null;
    }

    /// <summary>How large a face given no size of its own is drawn.</summary>
    public const double NaturalSize = 160;

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        var side = Math.Min(availableSize.Width, availableSize.Height);
        side = double.IsFinite(side) ? side : NaturalSize;
        if (side > 0 && Math.Abs(side - _side) > 0.5)
        {
            _side = side;
            Scale();
        }

        base.MeasureOverride(new Size<double>(side, side));
        return new Size<double>(side, side);
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        var side = Math.Min(finalSize.Width, finalSize.Height);
        var square = new Rect((finalSize.Width - side) / 2, (finalSize.Height - side) / 2, side, side);
        foreach (var visual in VisualChildren)
        {
            ((IMeasurableComponent)visual).Arrange(square);
        }

        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => null;

    private static void OnMarksChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) => ((ClockFace)a).Build();

    private static void OnChosenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) => ((ClockFace)a).Light();

    private void Build()
    {
        _marks.Clear();
        _labels.Clear();
        _innerLabels.Clear();
        _ticks.Clear();
        _dots.Clear();
        var style = FaceStyle;
        if (style is ClockFaceStyle.Classic or ClockFaceStyle.Roman or ClockFaceStyle.Minimal)
        {
            for (var minute = 0; minute < 60; minute++)
            {
                var hour = minute % 5 == 0;
                if (!hour && style == ClockFaceStyle.Minimal)
                {
                    continue;
                }

                var tick = new Border
                {
                    Classes = Classes.Parse(hour ? "ClockFaceHourTick" : "ClockFaceTick"),
                    RenderTransform = new Transform { RotationAngle = minute * 6 },
                    RenderTransformOrigin = new Vector2<double>(0.5, 0.5),
                    IsHitTestVisible = false,
                    Tag = hour,
                };
                _ticks.Add(tick);
                _marks.Add(tick, minute * 6, 0.92);
            }
        }

        if (style == ClockFaceStyle.Dots)
        {
            for (var hour = 0; hour < 12; hour++)
            {
                var dot = new Border { Classes = Classes.Parse("ClockFaceDot"), IsHitTestVisible = false };
                _dots.Add(dot);
                _marks.Add(dot, hour * 30, 0.86);
            }
        }

        var outer = Labels ?? style switch
        {
            ClockFaceStyle.Classic => Arabic,
            ClockFaceStyle.Roman => Roman,
            _ => null,
        };
        AddLabels(outer, style == ClockFaceStyle.Labels ? LabelRadius : 0.74, _labels);
        AddLabels(InnerLabels, InnerLabelRadius, _innerLabels);
        Scale();
        Light();
    }

    private void AddLabels(IReadOnlyList<string> texts, double radius, List<TextBlock> ring)
    {
        if (texts == null)
        {
            return;
        }

        for (var i = 0; i < 12; i++)
        {
            var label = new TextBlock
            {
                Text = i < texts.Count ? texts[i] : string.Empty,
                Classes = Classes.Parse("ClockFaceLabel"),
                HorizontalTextAlignment = HorizontalTextAlignment.Center,
                VerticalTextAlignment = VerticalTextAlignment.Center,
                IsHitTestVisible = false,
            };
            ring.Add(label);
            _marks.Add(label, i * 30, radius);
        }
    }

    private void Scale()
    {
        if (_side <= 0)
        {
            return;
        }

        var labelSide = _side * 0.14;
        foreach (var label in _labels.Concat(_innerLabels))
        {
            label.FontSize = Math.Max(8, _side * (FaceStyle == ClockFaceStyle.Roman ? 0.065 : 0.078));
            label.Width = label.Height = labelSide;
        }

        foreach (var tick in _ticks)
        {
            var hour = (bool)tick.Tag;
            tick.Width = Math.Max(1, _side * (hour ? 0.014 : 0.006));
            tick.Height = _side * (hour ? 0.06 : 0.03);
        }

        foreach (var dot in _dots)
        {
            dot.Width = dot.Height = Math.Max(3, _side * 0.03);
            dot.CornerRadius = new ProceduralGeometry.CornerRadius(dot.Width / 2);
        }
    }

    private void Light()
    {
        Light(_labels, ChosenLabel);
        Light(_innerLabels, ChosenLabel - 12);
    }

    private static void Light(List<TextBlock> ring, int chosen)
    {
        for (var i = 0; i < ring.Count; i++)
        {
            var name = i == chosen ? "ClockFaceLabelChosen" : "ClockFaceLabel";
            if (!ring[i].Classes.Contains(name))
            {
                ring[i].Classes = Classes.Parse(name);
            }
        }
    }
}
