using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The seat's drawing tablets (tablet-v2): every tool a pen pointer, raised when the compositor closes its
/// frame.</summary>
internal sealed class WaylandTablet
{
    private const uint ButtonStylus = 0x14b;
    private const float FullPressure = 65535f;

    private readonly WaylandPlatform platform;
    private readonly WaylandSeat seat;
    private readonly ZwpTabletSeatV2 tabletSeat;

    public WaylandTablet(WaylandPlatform platform, WaylandSeat seat, ZwpTabletSeatV2 tabletSeat)
    {
        this.platform = platform;
        this.seat = seat;
        this.tabletSeat = tabletSeat;
        tabletSeat.TabletAdded += tablet => tablet.Removed += tablet.Destroy;
        tabletSeat.ToolAdded += tool => _ = new Tool(this, tool);
        tabletSeat.PadAdded += pad => pad.Removed += pad.Destroy;
    }

    private sealed class Tool
    {
        private readonly WaylandTablet owner;
        private readonly ZwpTabletToolV2 proxy;
        private readonly WpCursorShapeDeviceV1 cursor;
        private ZwpTabletToolV2.Type type;
        private WaylandWindowWorker worker;
        private uint pointerId;
        private Vector2 position;
        private float pressure = PointerPointProperties.DefaultPressure;
        private Vector2 tilt;
        private float twist;
        private bool inContact;
        private bool barrel;
        private bool entered;
        private bool left;
        private bool wentDown;
        private bool wentUp;
        private bool moved;

        public Tool(WaylandTablet owner, ZwpTabletToolV2 proxy)
        {
            this.owner = owner;
            this.proxy = proxy;
            cursor = owner.platform.CursorShapes?.GetTabletToolV2(proxy);
            proxy.TypeEvent += value => type = value;
            proxy.ProximityIn += OnProximityIn;
            proxy.ProximityOut += () => left = true;
            proxy.Down += serial =>
            {
                owner.seat.NoteInput(serial, worker);
                inContact = wentDown = true;
            };
            proxy.Up += () =>
            {
                inContact = false;
                wentUp = true;
            };
            proxy.Motion += (x, y) =>
            {
                position = new Vector2(x, y);
                moved = true;
            };
            proxy.Pressure += value => pressure = value / FullPressure;
            proxy.Tilt += (x, y) => tilt = new Vector2(x, y);
            proxy.Rotation += degrees => twist = (float)degrees;
            proxy.Button += OnButton;
            proxy.Frame += OnFrame;
            proxy.Removed += () =>
            {
                cursor?.Destroy();
                proxy.Destroy();
            };
        }

        private void OnProximityIn(uint serial, ZwpTabletV2 tablet, WlSurface surface)
        {
            worker = owner.platform.Find(surface);
            pointerId = owner.seat.NextPointerId();
            entered = true;
            cursor?.SetShape(serial, WpCursorShapeDeviceV1.Shape.Default);
        }

        private void OnButton(uint serial, uint button, ZwpTabletToolV2.ButtonState state)
        {
            if (button == ButtonStylus)
            {
                barrel = state == ZwpTabletToolV2.ButtonState.Pressed;
                moved = true;
            }
        }

        private void OnFrame(uint time)
        {
            if (wentDown)
            {
                Raise(RawPointerEventType.Down, time);
            }

            if (wentUp)
            {
                Raise(RawPointerEventType.Up, time);
            }

            if (!wentDown && !wentUp && (moved || entered))
            {
                Raise(RawPointerEventType.Move, time);
            }

            if (left)
            {
                Raise(RawPointerEventType.LeaveWindow, time);
                worker = null;
                inContact = false;
            }

            entered = left = wentDown = wentUp = moved = false;
        }

        private void Raise(RawPointerEventType eventType, uint time)
        {
            var window = eventType is RawPointerEventType.Up or RawPointerEventType.LeaveWindow
                ? worker?.Window
                : worker?.InputWindow;
            if (window == null)
            {
                return;
            }

            var args = new RawPointerEventArgs(eventType, window, position, KeyboardDevice.CurrentDevice.Modifiers, time)
            {
                PointerId = pointerId,
                PointerType = PointerType.Pen,
                IsPrimary = true,
                Properties = new PointerPointProperties
                {
                    IsLeftButtonPressed = eventType == RawPointerEventType.Down || (eventType != RawPointerEventType.Up && inContact),
                    Pressure = pressure,
                    XTilt = (float)tilt.X,
                    YTilt = (float)tilt.Y,
                    Twist = twist,
                    IsEraser = type == ZwpTabletToolV2.Type.Eraser,
                    IsBarrelButtonPressed = barrel,
                },
            };
            WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
        }
    }
}
