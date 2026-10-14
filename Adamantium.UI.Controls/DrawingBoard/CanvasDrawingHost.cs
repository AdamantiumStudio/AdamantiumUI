using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Keeps the application's plane data and what draws it in step both ways, like <see cref="CanvasGraphHost"/>. The
/// application hands over data; the canvas builds its own items and controls.</summary>
internal sealed class CanvasDrawingHost
{
    private readonly Dictionary<ICanvasObject, ICanvasItem> _placed = new();
    private readonly Dictionary<ElementItem, ContentPresenter> _built = new();

    private IEnumerable _objects;
    private ICanvasScene _scene;
    private Adamantium.UI.Core.Templates.DataTemplateSelector _selector;

    /// <summary>What draws the ones that are not shapes. Handed to each control as it is made, and to those already
    /// standing when it arrives.</summary>
    public void SetTemplateSelector(Adamantium.UI.Core.Templates.DataTemplateSelector selector)
    {
        if (ReferenceEquals(_selector, selector)) return;

        _selector = selector;

        foreach (var (_, item) in _placed)
        {
            if (item is ElementItem { Element: ContentPresenter presenter })
            {
                presenter.ContentTemplateSelector = selector;
            }
        }
    }

    /// <summary>The application's collection. Followed while it is set, so one added to it appears on the plane and one
    /// taken out of it leaves.</summary>
    public void SetObjects(IEnumerable objects)
    {
        if (ReferenceEquals(_objects, objects)) return;

        if (_objects is INotifyCollectionChanged was) was.CollectionChanged -= OnObjectsChanged;

        Clear();
        _objects = objects;

        if (_objects is INotifyCollectionChanged now) now.CollectionChanged += OnObjectsChanged;

        Fill();
    }

    /// <summary>For a canvas that has been destroyed, or whose scene another canvas has taken: stops listening to the
    /// application's collection and objects, discards every control it built, takes its items off the scene, which the
    /// application keeps along with the items, and forgets the scene until it is given one again.</summary>
    public void StopFollowing()
    {
        if (_objects is INotifyCollectionChanged was)
        {
            was.CollectionChanged -= OnObjectsChanged;
        }

        foreach (var (item, presenter) in _built)
        {
            Discard(item, presenter);
        }

        _built.Clear();
        Clear();
        _scene = null;
    }

    public void SetScene(ICanvasScene scene)
    {
        if (ReferenceEquals(_scene, scene)) return;

        _scene = scene;

        // Heard again after a stop, which let go of the collection but not of which one it is.
        if (_scene != null && _objects is INotifyCollectionChanged objects)
        {
            objects.CollectionChanged -= OnObjectsChanged;
            objects.CollectionChanged += OnObjectsChanged;
        }

        Fill();
    }

    private void OnObjectsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            Clear();
            Fill();
            return;
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ICanvasObject model) Drop(model);
            }
        }

        if (e.NewItems == null) return;

        foreach (var item in e.NewItems)
        {
            if (item is ICanvasObject model) Put(model);
        }
    }

    private void Fill()
    {
        if (_objects == null || _scene == null) return;

        foreach (var item in _objects)
        {
            if (item is ICanvasObject model) Put(model);
        }
    }

    private void Put(ICanvasObject model)
    {
        if (_scene == null || _placed.ContainsKey(model)) return;

        // THE ITEM THE SCENE ALREADY HAS for this object, as for a node: see CanvasGraphHost.Put. Only its control is
        // this canvas's.
        var item = SceneItems.Of(_scene, model);
        if (!Fits(item, model))
        {
            item = Make(model);
            SceneItems.Keep(_scene, model, item);
        }

        if (item is ElementItem element)
        {
            if (_built.Remove(element, out var stale))
            {
                Discard(element, stale);
            }

            var presenter = Present(model);
            element.Element = presenter;
            _built[element] = presenter;
        }

        _placed[model] = item;
        model.PropertyChanged += OnModelChanged;

        _scene.Add(item);
    }

    // WHAT DRAWS IT, decided by what the application says it is. A shape the canvas knows how to draw; anything else is
    // the application's own object, and a control built here around it - the template says what that control looks
    // like, and the application never touches one.
    private ICanvasItem Make(ICanvasObject model)
    {
        var box = new Rect(model.Left, model.Top, model.Width, model.Height);

        if (model.Content is ICanvasShapeDescription shape)
        {
            return new ShapeItem(shape.Shape, box, shape.Stroke, shape.Thickness, shape.Fill)
            {
                Corner = shape.Corner,
                Sides = shape.Sides,
                StartHead = shape.StartHead,
                EndHead = shape.EndHead,
                Model = model
            };
        }

        return new ElementItem(null, box) { Model = model };
    }

    // Whether the item kept for an object still is what draws it: the application may have changed what it holds.
    private static bool Fits(ICanvasItem item, ICanvasObject model) =>
        model.Content is ICanvasShapeDescription shape
            ? item is ShapeItem drawn && drawn.Shape == shape.Shape
            : item is ElementItem;

    private ContentPresenter Present(ICanvasObject model) =>
        new()
        {
            Content = model.Content,
            ContentTemplateSelector = _selector
        };

    private void Drop(ICanvasObject model)
    {
        if (!_placed.Remove(model, out var item)) return;

        model.PropertyChanged -= OnModelChanged;
        _scene?.Remove(item);
    }

    private void Clear()
    {
        foreach (var (model, item) in _placed)
        {
            model.PropertyChanged -= OnModelChanged;
            _scene?.Remove(item);
        }

        _placed.Clear();
    }

    private static void Discard(ElementItem item, ContentPresenter presenter)
    {
        if (ReferenceEquals(item.Element, presenter))
        {
            item.Element = null;
        }

        DiscardedVisuals.Publish(presenter);
    }

    // Nothing is copied: the box a shape occupies IS the object's, read and written through. The scene is told that a
    // rectangle on it moved, because the plane is drawn from the items and nobody else would ask it to do that again.
    private void OnModelChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ICanvasObject.Left):
            case nameof(ICanvasObject.Top):
            case nameof(ICanvasObject.Width):
            case nameof(ICanvasObject.Height):
                (_scene as CanvasScene)?.Touch();
                break;
        }
    }
}
