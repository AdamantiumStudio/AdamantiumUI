using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Sandbox.Controls;

/// <summary>Draws every contact pressed on <see cref="Source"/> as a ring, as big as it presses - fingers and pens, as
/// many as are down - so what the system reported can be seen. Lies over the source and takes no input of its own; it
/// watches the source's events, handled ones too, so a button or a list taking a finger does not hide it.</summary>
public class TouchTrail : InputUIComponent
{
    public static readonly AdamantiumProperty SourceProperty = AdamantiumProperty.Register(nameof(Source),
        typeof(InputUIComponent), typeof(TouchTrail), new PropertyMetadata(null, OnSourceChanged));

    public static readonly AdamantiumProperty StrokeProperty = AdamantiumProperty.Register(nameof(Stroke),
        typeof(Brush), typeof(TouchTrail), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty ContactCountProperty = AdamantiumProperty.Register(nameof(ContactCount),
        typeof(int), typeof(TouchTrail), new PropertyMetadata(0));

    public static readonly AdamantiumProperty MostContactsProperty = AdamantiumProperty.Register(nameof(MostContacts),
        typeof(int), typeof(TouchTrail), new PropertyMetadata(0));

    public static readonly AdamantiumProperty ContactsProperty = AdamantiumProperty.Register(nameof(Contacts),
        typeof(string), typeof(TouchTrail), new PropertyMetadata(string.Empty));

    private readonly Dictionary<uint, (Vector2<double> At, float Pressure)> _down = new();
    private readonly PointerPressedEventHandler _onPressed;
    private readonly PointerEventHandler _onMoved;
    private readonly PointerReleasedEventHandler _onReleased;
    private readonly PointerEventHandler _onCanceled;

    public TouchTrail()
    {
        IsHitTestVisible = false;
        _onPressed = (_, e) => Put(e);
        _onMoved = (_, e) => Move(e);
        _onReleased = (_, e) => Lift(e);
        _onCanceled = (_, e) => Lift(e);
    }

    /// <summary>The element whose contacts are drawn; the trail lies over it.</summary>
    public InputUIComponent Source
    {
        get => GetValue<InputUIComponent>(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Brush Stroke
    {
        get => GetValue<Brush>(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>How many contacts are down now.</summary>
    public int ContactCount
    {
        get => GetValue<int>(ContactCountProperty);
        set => SetValue(ContactCountProperty, value);
    }

    /// <summary>The most contacts that were down at once; set it back to 0 to count again.</summary>
    public int MostContacts
    {
        get => GetValue<int>(MostContactsProperty);
        set => SetValue(MostContactsProperty, value);
    }

    /// <summary>The contacts down now, each as its pointer id and pressure.</summary>
    public string Contacts
    {
        get => GetValue<string>(ContactsProperty);
        set => SetValue(ContactsProperty, value);
    }

    private static void OnSourceChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not TouchTrail trail) return;

        if (e.OldValue is InputUIComponent old)
        {
            old.RemoveHandler(PointerEvents.PreviewPointerPressedEvent, trail._onPressed);
            old.RemoveHandler(PointerEvents.PreviewPointerMovedEvent, trail._onMoved);
            old.RemoveHandler(PointerEvents.PreviewPointerReleasedEvent, trail._onReleased);
            old.RemoveHandler(PointerEvents.PointerCanceledEvent, trail._onCanceled);
        }

        if (e.NewValue is InputUIComponent source)
        {
            source.AddHandler(PointerEvents.PreviewPointerPressedEvent, trail._onPressed, handledEventsToo: true);
            source.AddHandler(PointerEvents.PreviewPointerMovedEvent, trail._onMoved, handledEventsToo: true);
            source.AddHandler(PointerEvents.PreviewPointerReleasedEvent, trail._onReleased, handledEventsToo: true);
            source.AddHandler(PointerEvents.PointerCanceledEvent, trail._onCanceled, handledEventsToo: true);
        }
    }

    private void Put(PointerEventArgs e)
    {
        var live = PointerDevice.Current.Pointers.Select(pointer => pointer.Id).ToHashSet();
        foreach (var gone in _down.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _down.Remove(gone);
        }

        _down[e.Pointer.Id] = (e.GetPosition(this), e.Properties.Pressure);
        Changed();
    }

    private void Move(PointerEventArgs e)
    {
        if (!_down.ContainsKey(e.Pointer.Id)) return;

        _down[e.Pointer.Id] = (e.GetPosition(this), e.Properties.Pressure);
        Changed();
    }

    private void Lift(PointerEventArgs e)
    {
        if (_down.Remove(e.Pointer.Id)) Changed();
    }

    private void Changed()
    {
        SetCurrentValue(ContactCountProperty, _down.Count);
        if (_down.Count > MostContacts) SetCurrentValue(MostContactsProperty, _down.Count);
        SetCurrentValue(ContactsProperty,
            string.Join("  ", _down.OrderBy(c => c.Key).Select(c => $"#{c.Key} {c.Value.Pressure:0.00}")));
        InvalidateRender(false);
    }

    protected override void OnRender(IDrawingContext context)
    {
        if (Stroke == null) return;

        var session = context.ForControl(this);
        foreach (var (at, pressure) in _down.Values)
        {
            var radius = 16 + 24 * pressure;
            session.DrawEllipse(new Rect(at.X - radius, at.Y - radius, radius * 2, radius * 2), null, 0, 360,
                EllipseType.Sector, new Pen(Stroke, 2.5));
        }
    }
}
