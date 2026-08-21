using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class ObserverTests
{
    private sealed class Recorder : IToplevelObserver
    {
        public List<ulong> Added { get; } = [];

        public List<ulong> Changed { get; } = [];

        public List<ulong> Removed { get; } = [];

        public Action<ulong>? OnChangedExtra { get; set; }

        public void OnToplevelAdded(ulong toplevelId) => Added.Add(toplevelId);

        public void OnToplevelChanged(ulong toplevelId)
        {
            Changed.Add(toplevelId);
            OnChangedExtra?.Invoke(toplevelId);
        }

        public void OnToplevelRemoved(ulong toplevelId) => Removed.Add(toplevelId);
    }

    [Fact]
    public void Every_observer_sees_every_signal()
    {
        var observers = new ToplevelObservers();
        var first = new Recorder();
        var second = new Recorder();
        observers.Add(first);
        observers.Add(second);

        observers.Added(7);
        observers.Changed(7);
        observers.Removed(7);

        Assert.Equal([7ul], first.Added);
        Assert.Equal([7ul], first.Changed);
        Assert.Equal([7ul], first.Removed);
        Assert.Equal([7ul], second.Added);
    }

    [Fact]
    public void An_observer_that_removes_itself_mid_dispatch_does_not_disturb_the_walk()
    {
        var observers = new ToplevelObservers();
        var first = new Recorder();
        var second = new Recorder();
        var third = new Recorder();
        observers.Add(first);
        observers.Add(second);
        observers.Add(third);

        second.OnChangedExtra = _ => observers.Remove(second);

        observers.Changed(1);

        Assert.Equal([1ul], first.Changed);
        Assert.Equal([1ul], second.Changed);
        Assert.Equal([1ul], third.Changed);

        observers.Changed(2);

        Assert.Equal([1ul, 2ul], first.Changed);
        Assert.Equal([1ul], second.Changed);
        Assert.Equal([1ul, 2ul], third.Changed);
    }

    [Fact]
    public void An_observer_that_removes_another_mid_dispatch_does_not_disturb_the_walk()
    {
        var observers = new ToplevelObservers();
        var first = new Recorder();
        var second = new Recorder();
        var third = new Recorder();
        observers.Add(first);
        observers.Add(second);
        observers.Add(third);

        first.OnChangedExtra = _ => observers.Remove(third);

        observers.Changed(1);
        observers.Changed(2);

        Assert.Equal([1ul, 2ul], first.Changed);
        Assert.Equal([1ul, 2ul], second.Changed);
        Assert.Empty(third.Changed);
    }

    [Fact]
    public void Removing_an_observer_that_never_subscribed_does_nothing()
    {
        var observers = new ToplevelObservers();
        var subscribed = new Recorder();
        observers.Add(subscribed);
        observers.Remove(new Recorder());

        observers.Added(3);

        Assert.Equal([3ul], subscribed.Added);
    }

    [Fact]
    public void Dispatching_to_observers_allocates_nothing()
    {
        var observers = new ToplevelObservers();
        var recorder = new Recorder();
        observers.Add(recorder);

        for (var i = 0; i < 20; i++)
        {
            observers.Changed((ulong)i);
        }

        recorder.Changed.Clear();
        recorder.Changed.Capacity = 2048;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            observers.Changed((ulong)i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void The_aggregate_model_maps_source_ids_to_global_ones()
    {
        var model = new AggregateToplevelModel();
        var source = new TestToplevelModelSource();
        model.Add(source);

        var recorder = new Recorder();
        model.AddObserver(recorder);

        source.Raise(4);

        var id = Assert.Single(recorder.Added);
        Assert.NotEqual(4ul, id);
        Assert.True(model.TryGet(id, out var info));
        Assert.Equal("s", info.Title);
    }

    private sealed class TestToplevelModelSource : IToplevelSource
    {
        private readonly ToplevelObservers _observers = new();

        public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

        public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

        public void Raise(ulong localId) => _observers.Added(localId);

        public int Enumerate(Span<ToplevelInfo> toplevels)
        {
            if (toplevels.Length < 1)
            {
                return -1;
            }

            toplevels[0] = new ToplevelInfo(4, "s", "a", ToplevelState.None, null, default);
            return 1;
        }

        public bool TryGet(ulong localId, out ToplevelInfo info)
        {
            info = new ToplevelInfo(localId, "s", "a", ToplevelState.None, null, default);
            return localId == 4;
        }

        public bool Request(ulong localId, in ToplevelRequest request) => false;
    }
}
