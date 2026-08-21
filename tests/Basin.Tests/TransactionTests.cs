using Basin.Diagnostics;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public class TransactionTests
{
    [Fact]
    public void A_transaction_with_no_participants_completes_when_sealed()
    {
        using var host = new CompositorTestHost();
        using var transaction = new Transaction(host.Loop);
        var completed = 0;
        transaction.Completed += () => completed++;

        Assert.False(transaction.IsComplete);

        transaction.Seal();
        host.Loop.Dispatch(0);

        Assert.True(transaction.IsComplete);
        Assert.False(transaction.TimedOut);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void A_transaction_completes_when_the_last_participant_is_ready()
    {
        using var host = new CompositorTestHost();
        using var transaction = new Transaction(host.Loop);
        var a = transaction.Join();
        var b = transaction.Join();
        var c = transaction.Join();
        transaction.Seal();

        a.Ready();
        host.Loop.Dispatch(0);
        Assert.False(transaction.IsComplete);

        b.Ready();
        host.Loop.Dispatch(0);
        Assert.False(transaction.IsComplete);
        Assert.Equal(1, transaction.Outstanding);

        c.Ready();
        host.Loop.Dispatch(0);
        Assert.True(transaction.IsComplete);
        Assert.False(transaction.TimedOut);
        Assert.Equal(3, transaction.Participants);
    }

    [Fact]
    public void Joining_before_the_seal_holds_the_transaction_open()
    {
        using var host = new CompositorTestHost();
        using var transaction = new Transaction(host.Loop);

        var first = transaction.Join();
        first.Ready();
        host.Loop.Dispatch(0);
        Assert.False(transaction.IsComplete);

        var second = transaction.Join();
        transaction.Seal();
        host.Loop.Dispatch(0);
        Assert.False(transaction.IsComplete);

        second.Ready();
        host.Loop.Dispatch(0);
        Assert.True(transaction.IsComplete);
    }

    [Fact]
    public void Ready_is_idempotent()
    {
        using var host = new CompositorTestHost();
        using var transaction = new Transaction(host.Loop);
        var a = transaction.Join();
        var b = transaction.Join();
        transaction.Seal();

        a.Ready();
        a.Ready();
        a.Ready();
        host.Loop.Dispatch(0);

        Assert.False(transaction.IsComplete);
        Assert.Equal(1, transaction.Outstanding);

        b.Ready();
        host.Loop.Dispatch(0);
        Assert.True(transaction.IsComplete);
    }

    [Fact]
    public void Abandon_unblocks_a_transaction()
    {
        using var host = new CompositorTestHost();
        using var transaction = new Transaction(host.Loop);
        var alive = transaction.Join();
        var dead = transaction.Join();
        transaction.Seal();

        dead.Abandon();
        alive.Ready();
        host.Loop.Dispatch(0);

        Assert.True(transaction.IsComplete);
        Assert.False(transaction.TimedOut);
    }

    [Fact]
    public void A_transaction_completes_on_its_deadline_with_participants_outstanding()
    {
        using var host = new CompositorTestHost();
        Transaction.ResetCounters();
        using var transaction = new Transaction(host.Loop, timeoutMs: 5);
        _ = transaction.Join();
        transaction.Seal();

        var completed = 0;
        transaction.Completed += () => completed++;

        WaitForCompletion(host, transaction);

        Assert.True(transaction.TimedOut);
        Assert.Equal(1, completed);
        Assert.Equal(1, Transaction.TimedOutCount);
        Transaction.ResetCounters();
    }

    [Fact]
    public void A_transaction_completes_exactly_once_when_readiness_races_the_deadline()
    {
        using var host = new CompositorTestHost();
        Transaction.ResetCounters();
        using var transaction = new Transaction(host.Loop, timeoutMs: 5);
        var participant = transaction.Join();
        transaction.Seal();

        var completed = 0;
        transaction.Completed += () => completed++;

        WaitForCompletion(host, transaction);
        participant.Ready();
        host.Loop.Dispatch(0);
        host.Loop.Dispatch(0);

        Assert.Equal(1, completed);
        Transaction.ResetCounters();
    }

    [Fact]
    public void The_timed_out_counter_stays_at_zero_on_the_happy_path()
    {
        using var host = new CompositorTestHost();
        Transaction.ResetCounters();
        using var transaction = new Transaction(host.Loop, timeoutMs: 5000);
        var participant = transaction.Join();
        transaction.Seal();
        participant.Ready();
        host.Loop.Dispatch(0);

        Assert.True(transaction.IsComplete);
        Assert.Equal(0, Transaction.TimedOutCount);
    }

    private static void WaitForCompletion(CompositorTestHost host, Transaction transaction)
    {
        for (var i = 0; i < 100 && !transaction.IsComplete; i++)
        {
            host.Loop.Dispatch(20);
        }

        Assert.True(transaction.IsComplete, "the transaction never completed");
    }

    [Fact]
    public void Joining_a_sealed_transaction_is_rejected()
    {
        using var host = new CompositorTestHost();
        using var transaction = new Transaction(host.Loop);
        transaction.Seal();

        Assert.Throws<InvalidOperationException>(() => transaction.Join());
        host.Loop.Dispatch(0);
    }

    [Fact]
    public void A_transaction_in_flight_at_teardown_leaks_nothing()
    {
        LeakTracking.Require();
        using var host = new CompositorTestHost();
        var before = BasinCounters.LiveObjects;
        using (var transaction = new Transaction(host.Loop, timeoutMs: 100_000))
        {
            _ = transaction.Join();
            transaction.Seal();
            Assert.True(BasinCounters.LiveObjects > before);
        }

        host.Loop.Dispatch(0);
        Assert.Equal(before, BasinCounters.LiveObjects);
    }
}
