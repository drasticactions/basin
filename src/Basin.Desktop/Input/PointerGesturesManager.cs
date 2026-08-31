using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PointerGesturesManager : IDisposable
{
    public const int Version = 3;

    private readonly WlServerDisplay _display;
    private readonly Seat.Seat _seat;
    private readonly WlGlobal _global;
    private readonly List<ZwpPointerGestureSwipeV1Resource> _swipes = [];
    private readonly List<ZwpPointerGesturePinchV1Resource> _pinches = [];
    private readonly List<ZwpPointerGestureHoldV1Resource> _holds = [];

    public PointerGesturesManager(WlServerDisplay display, Seat.Seat seat)
    {
        _display = display;
        _seat = seat;
        _global = display.CreateGlobal(ZwpPointerGesturesV1.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

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
