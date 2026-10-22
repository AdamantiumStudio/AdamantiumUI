using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>A finger or a pen through a synthetic pointer device: the system raises real WM_POINTER messages for it.
/// One device per gesture, made and destroyed on the thread that plays it.</summary>
internal sealed class WindowsSyntheticPointerInput : ISyntheticPointerInput
{
    private const float FullPressure = 1024f;

    // About a frame apart, as a hand's reports come: the system coalesces frames that arrive closer.
    private static readonly TimeSpan FrameGap = TimeSpan.FromMilliseconds(16);

    private readonly IApplicationPlatform platform;

    public WindowsSyntheticPointerInput(IApplicationPlatform platform)
    {
        this.platform = platform;
    }

    public void Inject(IWindow window, PointerType type, IReadOnlyList<IReadOnlyList<SyntheticPointerFrame>> frames)
    {
        foreach (var contact in frames[0])
        {
            if (platform.WindowFromScreenPoint(contact.Screen) != window.Handle)
            {
                throw new InvalidOperationException(
                    $"({contact.Screen.X:0}, {contact.Screen.Y:0}) is not on top of the desktop: the system would press whatever is there.");
            }
        }

        var inputType = type switch
        {
            PointerType.Touch => PointerInputType.Touch,
            PointerType.Pen => PointerInputType.Pen,
            _ => throw new ArgumentException("Only a finger or a pen is a contact the system can be handed.", nameof(type))
        };

        var contacts = (uint)Math.Max(1, frames.Max(frame => frame.Count));
        var device = Win32Interop.CreateSyntheticPointerDevice(inputType, contacts, PointerFeedbackMode.Default);
        if (device == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"The system gave no synthetic {type} device (error {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            foreach (var frame in frames)
            {
                var infos = frame.Select((contact, index) => inputType == PointerInputType.Touch
                    ? Touch(contact, (uint)index)
                    : Pen(contact, (uint)index)).ToArray();
                if (!Win32Interop.InjectSyntheticPointerInput(device, infos, (uint)infos.Length))
                {
                    throw new InvalidOperationException(
                        $"The system refused a synthetic {type} frame (error {Marshal.GetLastWin32Error()}).");
                }

                Thread.Sleep(FrameGap);
            }
        }
        finally
        {
            Win32Interop.DestroySyntheticPointerDevice(device);
        }
    }

    private static POINTER_TYPE_INFO Touch(SyntheticPointerFrame frame, uint id) => POINTER_TYPE_INFO.OfTouch(new POINTER_TOUCH_INFO
    {
        pointerInfo = Info(PointerInputType.Touch, frame, id),
        touchMask = TouchMask.Pressure,
        pressure = (uint)(frame.Properties.Pressure * FullPressure)
    });

    private static POINTER_TYPE_INFO Pen(SyntheticPointerFrame frame, uint id)
    {
        // The side button and the eraser end press only while the tip touches; a pen held upside down says so in range.
        var touching = frame.Kind is RawPointerEventType.Down or RawPointerEventType.Move;
        var flags = PenFlags.None;
        if (frame.Properties.IsEraser) flags |= PenFlags.Inverted | (touching ? PenFlags.Eraser : PenFlags.None);
        if (frame.Properties.IsBarrelButtonPressed && touching) flags |= PenFlags.Barrel;

        return POINTER_TYPE_INFO.OfPen(new POINTER_PEN_INFO
        {
            pointerInfo = Info(PointerInputType.Pen, frame, id),
            penMask = PenMask.Pressure,
            pressure = touching ? (uint)(frame.Properties.Pressure * FullPressure) : 0,
            penFlags = flags
        });
    }

    private static POINTER_INFO Info(PointerInputType type, SyntheticPointerFrame frame, uint id) => new()
    {
        pointerType = type,
        pointerId = id,
        pointerFlags = Flags(type, frame.Kind),
        ptPixelLocation = new NativePoint((int)frame.Screen.X, (int)frame.Screen.Y)
    };

    // What each kind of frame says of the contact: in range and touching while it is down. A finger lifted is gone; a
    // pen lifted is still in range, hovering, until it leaves.
    private static PointerFlags Flags(PointerInputType type, RawPointerEventType kind) => kind switch
    {
        RawPointerEventType.Down => PointerFlags.InRange | PointerFlags.InContact | PointerFlags.Down,
        RawPointerEventType.Move => PointerFlags.InRange | PointerFlags.InContact | PointerFlags.Update,
        RawPointerEventType.Up when type == PointerInputType.Pen => PointerFlags.InRange | PointerFlags.Up,
        RawPointerEventType.Up => PointerFlags.Up,
        RawPointerEventType.LeaveWindow => PointerFlags.Update,
        _ => throw new ArgumentException($"A synthetic contact has no {kind} frame.", nameof(kind))
    };
}
