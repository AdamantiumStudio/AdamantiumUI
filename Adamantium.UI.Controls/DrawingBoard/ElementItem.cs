using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A real control on the plane at a world place and size. It costs a visual-tree node, which is why ink is not
/// one; panning and zooming only re-arrange it.</summary>
public class ElementItem : ICanvasItem, ICanvasTransformed
{
    private Boolean? _sizeFollowsContent;
    private Rect _world;

    public ElementItem(IUIComponent element, Rect world)
    {
        Element = element;
        World = world;
    }

    /// <summary>The control itself. It lives in the canvas's own visual tree while it is on screen, so it draws, takes
    /// input and animates the way it would anywhere else.</summary>
    public IUIComponent Element { get; }

    /// <summary>The control a person sees: for an application object, the templated child of the hosting
    /// <see cref="ContentPresenter"/>, where inspector edits must land.</summary>
    public IUIComponent Painted
    {
        get
        {
            if (Element is not ContentPresenter { VisualChildren.Count: 1 } presenter) return Element;

            foreach (var child in presenter.VisualChildren) return child;

            return Element;
        }
    }

    /// <summary>WHAT THIS STANDS FOR, when the canvas made it for a node of the application's own graph - null for a
    /// control somebody put on a drawing.
    /// <para>A container knows what it is showing, the way a list's row knows its item: an inspector pointed at the
    /// selection has to reach the node's own object, not the control drawn for it, or what it edits is the picture.
    /// </para></summary>
    public ICanvasPlaced Model { get; internal set; }

    /// <summary>Where it sits in world units. For a node container it reads and writes the node's own place, so the model
    /// holds the only copy; the size is the measured one.</summary>
    public Rect World
    {
        // NEVER NARROWER THAN WHAT IS IN IT, whoever asked. A grip is not the only way a width is written down - an
        // inspector line edits it and a file carries it - so the floor stands where the width is READ rather than at
        // each of the places it can be set, which is where one of them would eventually be forgotten.
        get => Model == null
            ? _world
            : new Rect(Model.Left, Model.Top,
                Math.Max(Model.Width > 0 ? Model.Width : _world.Width, Smallest.Width), _world.Height);
        set
        {
            _world = value;

            if (Model == null) return;

            Model.Left = value.X;
            Model.Top = value.Y;
        }
    }

    /// <summary>Whether the control decides the box, as a node does: height exactly as measured, width at least that. Most
    /// controls keep the size they were given.</summary>
    public Boolean SizeFollowsContent
    {
        get => _sizeFollowsContent ?? Element is CanvasNode;
        set => _sizeFollowsContent = value;
    }

    /// <summary>Where it stands in paint order - stamped by the scene. See ICanvasItem.Order.</summary>
    public int Order { get; set; }

    /// <summary>WHAT IS ON THE PLANE HERE - the control's own type name: "Image", "Button", "CanvasNode".
    /// <para>The thing itself and not the container round it, for the same reason <see cref="Painted"/> exists: what a
    /// person put down is the picture, not the presenter that carries it, and a panel asking what this is means the
    /// picture. See <see cref="ICanvasItem.Sort"/> for what it is for.</para></summary>
    public string Sort => (Painted ?? Element)?.GetType().Name ?? nameof(ElementItem);

    /// <summary>Whether a NAME OF ITS OWN means anything here - false for something standing for an object of the
    /// application's, which carries its own name and would then have two.
    /// <para>On the item rather than on whatever panel is showing it: a panel that had to know which kinds have their
    /// own name would have to be told about the next one too.</para></summary>
    public bool HasLabel => Model == null;

    public Rect Bounds => World;

    /// <summary>The least it may be resized to, in world units: measured for content-sized items, otherwise zero unless the
    /// application sets it.</summary>
    public Size Smallest { get; set; }

    /// <summary>A copy of a node (frame and sockets, not the application's content); null for any other control, which the
    /// engine cannot recreate.</summary>
    public ICanvasItem Copy()
    {
        if (Element is not CanvasNode node) return null;

        var made = new CanvasNode
        {
            Kind = node.Kind,
            Title = node.Title,
            Accent = node.Accent,
            PinColor = node.PinColor,
            IsCollapsed = node.IsCollapsed,
            Inputs = node.InputPins.Count,
            Outputs = node.OutputPins.Count
        };

        Dress(node.InputPins, made.InputPins);
        Dress(node.OutputPins, made.OutputPins);

        return new ElementItem(made, World) { SizeFollowsContent = SizeFollowsContent };
    }

    private static void Dress(System.Collections.ObjectModel.ObservableCollection<CanvasNodePin> from,
        System.Collections.ObjectModel.ObservableCollection<CanvasNodePin> to)
    {
        for (var i = 0; i < from.Count && i < to.Count; i++)
        {
            to[i].Name = from[i].Name;
            to[i].Color = from[i].Color;
            to[i].Kind = from[i].Kind;
        }
    }

    /// <summary>A NODE belongs to a graph; every other control on the plane - a button, a field, a check box - is part
    /// of the drawing it was put on. Decided by what is being hosted and not by a flag on the item: which of the two a
    /// control is, is a fact about the control.</summary>
    public CanvasMode Mode => Element is CanvasNode ? CanvasMode.Nodes : CanvasMode.Drawing;

    /// <summary>The control's own type, and what it says if it says anything: a page holding six buttons needs to tell
    /// them apart, and the only thing that does is the words on them.</summary>
    public string Title
    {
        get
        {
            // The control a person put down, not the presenter carrying it - the same rule as Sort. Read off Element,
            // every hosted object on the plane was called a ContentPresenter.
            var kind = (Painted ?? Element)?.GetType().Name ?? "Element";
            var says = Label;

            return string.IsNullOrWhiteSpace(says) ? kind : $"{kind} \"{says}\"";
        }
    }

    /// <summary>Where and how big, one number at a time. <see cref="World"/> is a rectangle and a rectangle cannot be
    /// half-written, so an inspector line that edits only the X of one has nothing to bind to - these are that line.
    /// </summary>
    public Double X
    {
        get => World.X;
        set => World = new Rect(value, World.Y, World.Width, World.Height);
    }

    public Double Y
    {
        get => World.Y;
        set => World = new Rect(World.X, value, World.Width, World.Height);
    }

    public Double Width
    {
        get => World.Width;
        set => World = new Rect(World.X, World.Y, Math.Max(1, value), World.Height);
    }

    public Double Height
    {
        get => World.Height;
        set => World = new Rect(World.X, World.Y, World.Width, Math.Max(1, value));
    }

    // The shown control's CornerRadius, looked up by name as a binding would; exposed per corner since a struct cannot be
    // bound a quarter at a time.
    private CornerRadius Corners
    {
        get => Rounded(out var component, out var property) ? (CornerRadius)component.GetValue(property) : default;
        set
        {
            if (Rounded(out var component, out var property)) component.SetValue(property, value);
        }
    }

    private bool Rounded(out IAdamantiumComponent component, out AdamantiumProperty property)
    {
        component = Painted as IAdamantiumComponent;
        property = component?.GetProperty(nameof(Control.CornerRadius));

        return property != null && property.PropertyType == typeof(CornerRadius);
    }

    public Double CornerTopLeft
    {
        get => Corners.TopLeft;
        set => SetCorner(value, Corners, 0);
    }

    public Double CornerTopRight
    {
        get => Corners.TopRight;
        set => SetCorner(value, Corners, 1);
    }

    public Double CornerBottomRight
    {
        get => Corners.BottomRight;
        set => SetCorner(value, Corners, 2);
    }

    public Double CornerBottomLeft
    {
        get => Corners.BottomLeft;
        set => SetCorner(value, Corners, 3);
    }


    public Double BorderWidth
    {
        get => Painted is Control control ? control.BorderThickness.Left : 0;
        set
        {
            if (Painted is Control control) control.BorderThickness = new Thickness(Math.Max(0, value));
        }
    }

    /// <summary>What the control says - its content, its text, or a node's title - for an inspector line; null for a control
    /// with no label.</summary>
    public String Label
    {
        // THE CONTROL, not the host it stands in: an application's object is hosted inside a ContentPresenter, and
        // that presenter's content is the application's OBJECT - so a label read off it was the object's ToString and
        // a label written onto it replaced the object with a string. What a person means by "what it says" is what the
        // button says. See Painted.
        get => Painted switch
        {
            TextBox box => box.Text,
            // Before the content control: a node is not one, so without this it answered nothing and read in the
            // structure list as a nameless "CanvasNode" - in a graph of fifty, fifty times over.
            CanvasNode node => node.Title?.ToString(),
            IContentControl content => content.Content?.ToString(),
            _ => null
        };
        set
        {
            switch (Painted)
            {
                case TextBox box:
                    box.Text = value;
                    break;

                case CanvasNode node:
                    node.Title = value;
                    break;

                case IContentControl content:
                    content.Content = value;
                    break;
            }
        }
    }

    private Vector2 Middle => new(World.X + World.Width / 2, World.Y + World.Height / 2);

    private CanvasTransform _transform = CanvasTransform.None;

    /// <summary>The control's rotation and skew about its middle, applied by the layer inside its one zoom render transform,
    /// so size and hit-testing stay right.</summary>
    public CanvasTransform Transform
    {
        get => _transform;
        set => _transform = value;
    }

    public double Angle
    {
        get => Transform.Angle;
        set => Transform = Transform with { Angle = value };
    }

    public double SkewX
    {
        get => Transform.SkewX;
        set => Transform = Transform with { SkewX = value };
    }

    public double SkewY
    {
        get => Transform.SkewY;
        set => Transform = Transform with { SkewY = value };
    }

    public void Move(Vector2 worldDelta) =>
        World = new Rect(World.X + worldDelta.X, World.Y + worldDelta.Y, World.Width, World.Height);

    public void Resize(Rect world)
    {
        if (world.Width <= 0 || world.Height <= 0) return;

        // NEVER UNDER WHAT IS INSIDE IT. The layer already refuses to MEASURE a node narrower than its own contents,
        // but for a node that width is kept on the model and the layer's correction never reached it - so a grip could
        // write a width of nothing onto the node and the node was drawn spilling out of a frame two pixels wide.
        var width = Math.Max(world.Width, Smallest.Width);
        var height = Math.Max(world.Height, Smallest.Height);

        World = new Rect(world.X, world.Y, width, height);

        // A width ASKED FOR, which the measured one is not: the hand on the edge is the only thing that says a node is
        // to be wider than it needs, so it is the only thing that writes it down.
        if (Model != null) Model.Width = width;
    }

    public bool HitTest(Vector2 world, double tolerance)
    {
        // TURNED BACK FIRST, the way a shape does it: the box is stated straight, so a point is asked about in the
        // control's own frame rather than the frame it is drawn in.
        if (_transform.IsSomething) world = _transform.Undo(world, Middle);

        return world.X >= World.X - tolerance && world.X <= World.X + World.Width + tolerance &&
               world.Y >= World.Y - tolerance && world.Y <= World.Y + World.Height + tolerance;
    }

    /// <summary>Nothing: a control draws ITSELF, from its own template, as a child of the canvas. Everything else on the
    /// plane is data and has to be painted here; this one is the case that is not.</summary>
    public void Render(IDrawingSession session, InfiniteCanvas canvas)
    {
    }

    /// <summary>One tile's rectangle as a fraction of what it paints, exposed per side since a Rect cannot be bound a side
    /// at a time; zero without a picture.</summary>
    public Double TileX
    {
        get => Tiled?.Viewport.X ?? 0;
        set => SetTile(value, 0);
    }

    public Double TileY
    {
        get => Tiled?.Viewport.Y ?? 0;
        set => SetTile(value, 1);
    }

    public Double TileWidth
    {
        get => Tiled?.Viewport.Width ?? 0;
        set => SetTile(value, 2);
    }

    public Double TileHeight
    {
        get => Tiled?.Viewport.Height ?? 0;
        set => SetTile(value, 3);
    }

    /// <summary>The thing being shown, WHERE IT SHOWS A PICTURE - which is what the lines about a texture are about.
    /// Not "is this a texture": a texture is an ordinary picture control put on the plane, and a picture put there any
    /// other way has the same questions to answer.</summary>
    public Image Tiled => Painted as Image;

    private void SetTile(double value, int side)
    {
        if (Tiled is not { } brush) return;

        var box = brush.Viewport;

        // A tile of no width is a picture that never lands, and the brush would go on being asked for copies of
        // nothing. The smallest tile is one pixel's worth of the shape, which is as small as anybody means.
        brush.Viewport = side switch
        {
            0 => new Rect(value, box.Y, box.Width, box.Height),
            1 => new Rect(box.X, value, box.Width, box.Height),
            2 => new Rect(box.X, box.Y, Math.Max(0.001, value), box.Height),
            _ => new Rect(box.X, box.Y, box.Width, Math.Max(0.001, value))
        };
    }

    private void SetCorner(double value, CornerRadius current, int corner)
    {
        value = Math.Max(0, value);
        Corners = corner switch
        {
            0 => new CornerRadius(value, current.TopRight, current.BottomRight, current.BottomLeft),
            1 => new CornerRadius(current.TopLeft, value, current.BottomRight, current.BottomLeft),
            2 => new CornerRadius(current.TopLeft, current.TopRight, value, current.BottomLeft),
            _ => new CornerRadius(current.TopLeft, current.TopRight, current.BottomRight, value)
        };
    }
}
