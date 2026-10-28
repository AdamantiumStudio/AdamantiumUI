using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The seat's touchscreen: every contact a pointer of its own, raised when the compositor closes its frame.</summary>
internal sealed class WaylandTouch
{
    private readonly WaylandPlatform platform;
    private readonly WaylandSeat seat;
    private readonly WlTouch touch;
    private readonly Dictionary<int, Contact> contacts = [];
    private readonly List<TouchEvent> pending = [];

    public WaylandTouch(WaylandPlatform platform, WaylandSeat seat, WlTouch touch)
    {
        this.platform = platform;
        this.seat = seat;
        this.touch = touch;
        touch.Down += OnDown;
        touch.Motion += OnMotion;
        touch.Up += OnUp;
        touch.Shape += OnShape;
        touch.Orientation += OnOrientation;
        touch.Frame += OnFrame;
        touch.Cancel += OnCancel;
    }

    /// <summary>Raises what came since the last frame: a compositor may send the last up of a gesture with no frame
    /// after it.</summary>
    public void Flush() => OnFrame();

    public void Release()
    {
        OnCancel();
        touch.Release();
    }

    private void OnDown(uint serial, uint time, WlSurface surface, int id, double x, double y)
    {
        var worker = platform.Find(surface);
        seat.NoteInput(serial, worker);
        var position = new Vector2<double>(x, y);
        if (seat.TouchSuppressed || worker?.InputWindow == null || worker.BeginResize(worker.ResizeEdgeAt(position), serial))
        {
            contacts[id] = new Contact(0, null, false) { Ignored = true };
            return;
        }

        var contact = new Contact(seat.NextPointerId(), worker, IsFirst()) { Position = position };
        contacts[id] = contact;
        Queue(RawPointerEventType.Down, contact, time);
    }

    private void OnMotion(uint time, int id, double x, double y)
    {
        if (contacts.TryGetValue(id, out var contact) && !contact.Ignored)
        {
            contact.Position = new Vector2<double>(x, y);
            Queue(RawPointerEventType.Move, contact, time);
        }
    }

    private void OnUp(uint serial, uint time, int id)
    {
        if (contacts.Remove(id, out var contact) && !contact.Ignored)
        {
            contact.InContact = false;
            Queue(RawPointerEventType.Up, contact, time);
        }
    }

    private void OnShape(int id, double major, double minor)
    {
        if (contacts.TryGetValue(id, out var contact) && !contact.Ignored)
        {
            contact.Axes = new Vector2<double>(major, minor);
            Reshape(contact);
        }
    }

    private void OnOrientation(int id, double orientation)
    {
        if (contacts.TryGetValue(id, out var contact) && !contact.Ignored)
        {
            contact.Orientation = orientation;
            Reshape(contact);
        }
    }

    private void OnFrame()
    {
        foreach (var queued in pending)
        {
            Raise(queued);
        }

        pending.Clear();
    }

    private void OnCancel()
    {
        OnFrame();
        foreach (var contact in contacts.Values)
        {
            if (!contact.Ignored)
            {
                contact.InContact = false;
                Raise(Snapshot(RawPointerEventType.Cancel, contact, 0));
            }
        }

        contacts.Clear();
    }

    private bool IsFirst()
    {
        foreach (var contact in contacts.Values)
        {
            if (!contact.Ignored)
            {
                return false;
            }
        }

        return true;
    }

    private void Queue(RawPointerEventType type, Contact contact, uint time) => pending.Add(Snapshot(type, contact, time));

    private void Reshape(Contact contact)
    {
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i].PointerId == contact.PointerId)
            {
                pending[i] = pending[i] with { Area = Area(contact) };
                return;
            }
        }
    }

    private static TouchEvent Snapshot(RawPointerEventType type, Contact contact, uint time) =>
        new(type, contact.PointerId, contact.Worker, contact.IsPrimary, contact.Position, time, contact.InContact, Area(contact));

    private static Rect Area(Contact contact)
    {
        if (contact.Axes == Vector2<double>.Zero)
        {
            return default;
        }

        var angle = contact.Orientation * Math.PI / 180;
        var major = contact.Axes.X / 2;
        var minor = contact.Axes.Y / 2;
        var halfWidth = Math.Sqrt(Math.Pow(major * Math.Sin(angle), 2) + Math.Pow(minor * Math.Cos(angle), 2));
        var halfHeight = Math.Sqrt(Math.Pow(major * Math.Cos(angle), 2) + Math.Pow(minor * Math.Sin(angle), 2));
        return new Rect(contact.Position.X - halfWidth, contact.Position.Y - halfHeight, halfWidth * 2, halfHeight * 2);
    }

    private static void Raise(TouchEvent touchEvent)
    {
        var window = touchEvent.Worker.Window;
        if (window == null)
        {
            return;
        }

        var args = new RawPointerEventArgs(touchEvent.Type, window, touchEvent.Position, KeyboardDevice.CurrentDevice.Modifiers,
            touchEvent.Time)
        {
            PointerId = touchEvent.PointerId,
            PointerType = PointerType.Touch,
            IsPrimary = touchEvent.IsPrimary,
            Properties = new PointerPointProperties
            {
                IsLeftButtonPressed = touchEvent.InContact,
                Pressure = PointerPointProperties.DefaultPressure,
                ContactRect = touchEvent.Area,
            },
        };
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private sealed class Contact(uint pointerId, WaylandWindowWorker worker, bool isPrimary)
    {
        public uint PointerId { get; } = pointerId;

        public WaylandWindowWorker Worker { get; } = worker;

        public bool IsPrimary { get; } = isPrimary;

        public bool Ignored { get; init; }

        public bool InContact { get; set; } = true;

        public Vector2<double> Position { get; set; }

        public Vector2<double> Axes { get; set; }

        public double Orientation { get; set; }
    }

    private readonly record struct TouchEvent(
        RawPointerEventType Type, uint PointerId, WaylandWindowWorker Worker, bool IsPrimary, Vector2<double> Position, uint Time,
        bool InContact, Rect Area);
}
