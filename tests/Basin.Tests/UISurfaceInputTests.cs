using Basin.Capabilities;
using Basin.UI.Skia;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class UISurfaceInputTests
{
    [Fact]
    public void An_existing_surface_answers_the_new_members_by_default()
    {
        using var host = new SkiaUIHost();
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 64,
            Height = 32,
            Scale = 1.0,
        });
        Assert.NotNull(surface);

        Assert.False(surface.WantsTextInput);
        Drive(surface);
        surface.Dispose();
    }

    [Fact]
    public void A_falsifier_surface_answers_the_new_members_by_default()
    {
        using var host = new FalsifierUIHost();
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 64,
            Height = 32,
            Scale = 1.0,
        });
        Assert.NotNull(surface);

        Assert.False(surface.WantsTextInput);
        Drive(surface);
        surface.Dispose();
    }

    [Fact]
    public void An_overriding_surface_receives_every_call()
    {
        using IUISurface surface = new RecordingUISurface();
        var recorder = (RecordingUISurface)surface;

        surface.NotifyKeyboardEnter([30u, 31u]);
        surface.NotifyModifiers(4u, 0u, 2u, 1u);
        surface.NotifyKey(11u, 30u, pressed: true);
        surface.NotifyKey(12u, 30u, pressed: false);
        surface.NotifyKeyboardLeave();
        surface.NotifyTouchDown(20u, 7, 12.5, 8.25);
        surface.NotifyTouchMotion(21u, 7, 13.5, 9.25);
        surface.NotifyTouchUp(22u, 7);
        surface.NotifyTouchCancel();
        surface.NotifyPreedit("かん", 0, 2);
        surface.NotifyTextCommit("漢");

        Assert.Equal(
            "enter:30,31|mods:4,0,2,1|key:30+|key:30-|leave|down:7@12.5,8.25|motion:7@13.5,9.25|up:7|cancel|preedit:かん[0,2]|commit:漢",
            recorder.Log);
        Assert.True(surface.WantsTextInput);
    }

    private static void Drive(IUISurface surface)
    {
        surface.NotifyKeyboardEnter([30u]);
        surface.NotifyModifiers(0u, 0u, 0u, 0u);
        surface.NotifyKey(1u, 30u, pressed: true);
        surface.NotifyKey(2u, 30u, pressed: false);
        surface.NotifyKeyboardLeave();
        surface.NotifyTouchDown(3u, 0, 1.0, 2.0);
        surface.NotifyTouchMotion(4u, 0, 3.0, 4.0);
        surface.NotifyTouchUp(5u, 0);
        surface.NotifyTouchCancel();
        surface.NotifyPreedit("ab", 0, 2);
        surface.NotifyTextCommit("ab");
    }

    private sealed class RecordingUISurface : IUISurface
    {
        private readonly System.Text.StringBuilder _log = new();

        public string Log => _log.ToString();

        public UISurfaceSize Size => new(0, 0, 1.0);

        public bool WantsTextInput => true;

        public bool Configure(int logicalWidth, int logicalHeight, double scale) => false;

        public bool TryAcquire(out UIFrame frame)
        {
            frame = default;
            return false;
        }

        public void AddObserver(IUISurfaceObserver observer)
        {
        }

        public void RemoveObserver(IUISurfaceObserver observer)
        {
        }

        public bool AcceptsInputAt(double x, double y) => true;

        public string? CursorAt(double x, double y) => null;

        public void NotifyPointerEnter(double x, double y)
        {
        }

        public void NotifyPointerMotion(uint timeMs, double x, double y)
        {
        }

        public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
        {
        }

        public void NotifyPointerAxis(uint timeMs, double dx, double dy)
        {
        }

        public void NotifyPointerLeave()
        {
        }

        public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
        {
            Separate();
            _log.Append("enter:");
            for (var i = 0; i < pressed.Length; i++)
            {
                if (i > 0)
                {
                    _log.Append(',');
                }

                _log.Append(pressed[i]);
            }
        }

        public void NotifyKey(uint timeMs, uint key, bool pressed)
        {
            Separate();
            _log.Append("key:").Append(key).Append(pressed ? '+' : '-');
        }

        public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
        {
            Separate();
            _log.Append("mods:").Append(depressed).Append(',').Append(latched)
                .Append(',').Append(locked).Append(',').Append(group);
        }

        public void NotifyKeyboardLeave()
        {
            Separate();
            _log.Append("leave");
        }

        public void NotifyTouchDown(uint timeMs, int id, double x, double y)
        {
            Separate();
            _log.Append("down:").Append(id).Append('@').Append(x).Append(',').Append(y);
        }

        public void NotifyTouchMotion(uint timeMs, int id, double x, double y)
        {
            Separate();
            _log.Append("motion:").Append(id).Append('@').Append(x).Append(',').Append(y);
        }

        public void NotifyTouchUp(uint timeMs, int id)
        {
            Separate();
            _log.Append("up:").Append(id);
        }

        public void NotifyTouchCancel()
        {
            Separate();
            _log.Append("cancel");
        }

        public void NotifyTextCommit(ReadOnlySpan<char> text)
        {
            Separate();
            _log.Append("commit:").Append(text);
        }

        public void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
        {
            Separate();
            _log.Append("preedit:").Append(text).Append('[').Append(cursorBegin)
                .Append(',').Append(cursorEnd).Append(']');
        }

        public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

        public void Dispose()
        {
        }

        private void Separate()
        {
            if (_log.Length > 0)
            {
                _log.Append('|');
            }
        }
    }
}
