using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_pointer_gestures_v1</c> global. The consumer reports gestures, and the manager sends them to the client with pointer focus. Each call does nothing when no surface has pointer focus.
/// </summary>
public sealed class PointerGesturesManager : IDisposable
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 3;

    private readonly WlServerDisplay _display;
    private readonly Seat.Seat _seat;
    private readonly WlGlobal _global;
    private readonly List<ZwpPointerGestureSwipeV1Resource> _swipes = [];
    private readonly List<ZwpPointerGesturePinchV1Resource> _pinches = [];
    private readonly List<ZwpPointerGestureHoldV1Resource> _holds = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerGesturesManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="seat">The seat whose pointer focus selects the client.</param>
    public PointerGesturesManager(WlServerDisplay display, Seat.Seat seat)
    {
        _display = display;
        _seat = seat;
        _global = display.CreateGlobal(ZwpPointerGesturesV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Removes the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Starts a swipe gesture on the focused surface.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="fingers">The number of fingers.</param>
    public void NotifySwipeBegin(uint timeMs, uint fingers)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _swipes)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendBegin(_display.NextSerial(), timeMs, surface.Resource, fingers);
            }
        }
    }

    /// <summary>
    /// Reports the motion of a swipe gesture since the last update.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The X delta in surface-local logical pixels.</param>
    /// <param name="dy">The Y delta in surface-local logical pixels.</param>
    public void NotifySwipeUpdate(uint timeMs, double dx, double dy)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _swipes)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendUpdate(timeMs, WlFixed.FromDouble(dx), WlFixed.FromDouble(dy));
            }
        }
    }

    /// <summary>
    /// Ends a swipe gesture.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="canceled"><see langword="true"/> when the gesture was canceled rather than completed.</param>
    public void NotifySwipeEnd(uint timeMs, bool canceled = false)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _swipes)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendEnd(_display.NextSerial(), timeMs, canceled ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// Starts a pinch gesture on the focused surface.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="fingers">The number of fingers.</param>
    public void NotifyPinchBegin(uint timeMs, uint fingers)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _pinches)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendBegin(_display.NextSerial(), timeMs, surface.Resource, fingers);
            }
        }
    }

    /// <summary>
    /// Reports the state of a pinch gesture.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The X delta of the gesture center since the last update, in surface-local logical pixels.</param>
    /// <param name="dy">The Y delta of the gesture center since the last update, in surface-local logical pixels.</param>
    /// <param name="scale">The scale relative to the start of the gesture, where 1 is no change.</param>
    /// <param name="rotation">The rotation since the last update, in degrees clockwise.</param>
    public void NotifyPinchUpdate(uint timeMs, double dx, double dy, double scale, double rotation)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _pinches)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendUpdate(
                    timeMs,
                    WlFixed.FromDouble(dx),
                    WlFixed.FromDouble(dy),
                    WlFixed.FromDouble(scale),
                    WlFixed.FromDouble(rotation));
            }
        }
    }

    /// <summary>
    /// Ends a pinch gesture.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="canceled"><see langword="true"/> when the gesture was canceled rather than completed.</param>
    public void NotifyPinchEnd(uint timeMs, bool canceled = false)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _pinches)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendEnd(_display.NextSerial(), timeMs, canceled ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// Starts a hold gesture on the focused surface. The client receives it only from version 3 of the global.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="fingers">The number of fingers.</param>
    public void NotifyHoldBegin(uint timeMs, uint fingers)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _holds)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendBegin(_display.NextSerial(), timeMs, surface.Resource, fingers);
            }
        }
    }

    /// <summary>
    /// Ends a hold gesture.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="canceled"><see langword="true"/> when the gesture was canceled rather than completed.</param>
    public void NotifyHoldEnd(uint timeMs, bool canceled = false)
    {
        if (_seat.Pointer.Focus is not { } surface)
        {
            return;
        }

        foreach (var resource in _holds)
        {
            if (!resource.IsDestroyed && resource.Client == surface.Resource.Client)
            {
                resource.SendEnd(_display.NextSerial(), timeMs, canceled ? 1 : 0);
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpPointerGesturesV1Resource(client, version, id);
        manager.GetSwipeGesture += (_, e) => Track(_swipes, new ZwpPointerGestureSwipeV1Resource(client, manager.Version, e.Id));
        manager.GetPinchGesture += (_, e) => Track(_pinches, new ZwpPointerGesturePinchV1Resource(client, manager.Version, e.Id));
        manager.GetHoldGesture += (_, e) => Track(_holds, new ZwpPointerGestureHoldV1Resource(client, manager.Version, e.Id));
    }

    private static void Track<T>(List<T> list, T resource)
        where T : WlResource
    {
        list.Add(resource);
        resource.Destroyed += (_, _) => list.Remove(resource);
    }
}
