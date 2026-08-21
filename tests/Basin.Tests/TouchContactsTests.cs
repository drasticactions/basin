using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public class TouchContactsTests
{
    private static TouchContacts WithThree()
    {
        var contacts = new TouchContacts();
        contacts.Down(0, 100, 100);
        contacts.Down(1, 200, 100);
        contacts.Down(2, 300, 100);
        return contacts;
    }

    [Fact]
    public void Count_follows_down_and_up()
    {
        var contacts = new TouchContacts();

        Assert.Equal(0, contacts.Count);
        contacts.Down(0, 10, 10);
        Assert.Equal(1, contacts.Count);
        contacts.Down(1, 20, 20);
        Assert.Equal(2, contacts.Count);
        Assert.True(contacts.Up(0));
        Assert.Equal(1, contacts.Count);
    }

    [Fact]
    public void A_repeated_down_moves_the_contact_rather_than_adding_one()
    {
        var contacts = new TouchContacts();
        contacts.Down(0, 10, 10);

        contacts.Down(0, 50, 50);

        Assert.Equal(1, contacts.Count);
        Assert.True(contacts.TryCentroid(out var x, out var y));
        Assert.Equal(50, x, 6);
        Assert.Equal(50, y, 6);
    }

    [Fact]
    public void One_contact_moves_the_centre_by_its_own_movement()
    {
        var contacts = new TouchContacts();
        contacts.Down(0, 100, 100);

        Assert.True(contacts.Motion(0, 130, 90, out var dx, out var dy));

        Assert.Equal(30, dx, 6);
        Assert.Equal(-10, dy, 6);
    }

    [Fact]
    public void Contacts_moving_together_move_the_centre_the_same_distance()
    {
        var contacts = WithThree();

        contacts.Motion(0, 130, 100, out _, out _);
        contacts.Motion(1, 230, 100, out _, out _);
        var moved = contacts.Motion(2, 330, 100, out var dx, out _);

        Assert.True(moved);
        Assert.Equal(10, dx, 6);
        Assert.True(contacts.TryCentroid(out var x, out _));
        Assert.Equal(230, x, 6);
    }

    [Fact]
    public void One_contact_of_three_moves_the_centre_by_a_third()
    {
        var contacts = WithThree();

        Assert.True(contacts.Motion(0, 130, 100, out var dx, out var dy));

        Assert.Equal(10, dx, 6);
        Assert.Equal(0, dy, 6);
    }

    [Fact]
    public void A_contact_landing_mid_drag_reports_no_movement()
    {
        var contacts = new TouchContacts();
        contacts.Down(0, 100, 100);
        contacts.Down(1, 200, 100);
        contacts.Motion(0, 110, 100, out _, out _);

        contacts.Down(2, 900, 100);

        Assert.True(contacts.Motion(0, 140, 100, out var dx, out _));
        Assert.Equal(10, dx, 6);
    }

    [Fact]
    public void A_contact_lifting_mid_drag_reports_no_movement()
    {
        var contacts = WithThree();
        contacts.Motion(0, 130, 100, out _, out _);

        Assert.True(contacts.Up(2));

        Assert.True(contacts.Motion(0, 160, 100, out var dx, out _));
        Assert.Equal(15, dx, 6);
    }

    [Fact]
    public void An_unknown_contact_reports_nothing_and_changes_nothing()
    {
        var contacts = WithThree();

        Assert.False(contacts.Motion(7, 900, 900, out var dx, out var dy));
        Assert.False(contacts.Up(7));

        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
        Assert.Equal(3, contacts.Count);
        Assert.True(contacts.TryCentroid(out var x, out _));
        Assert.Equal(200, x, 6);
    }

    [Fact]
    public void Contacts_beyond_the_capacity_are_dropped()
    {
        var contacts = new TouchContacts();
        for (var i = 0; i < TouchContacts.Capacity; i++)
        {
            contacts.Down(i, 100, 100);
        }

        contacts.Down(99, 5000, 5000);

        Assert.Equal(TouchContacts.Capacity, contacts.Count);
        Assert.True(contacts.TryCentroid(out var x, out var y));
        Assert.Equal(100, x, 6);
        Assert.Equal(100, y, 6);
        Assert.False(contacts.Motion(99, 6000, 6000, out _, out _));
    }

    [Fact]
    public void Clear_empties_the_tracker()
    {
        var contacts = WithThree();

        contacts.Clear();

        Assert.Equal(0, contacts.Count);
        Assert.False(contacts.TryCentroid(out _, out _));
        Assert.False(contacts.Motion(0, 10, 10, out _, out _));
    }

    [Fact]
    public void A_slot_freed_by_an_up_is_reused()
    {
        var contacts = WithThree();

        Assert.True(contacts.Up(0));
        contacts.Down(3, 300, 100);

        Assert.Equal(3, contacts.Count);
        Assert.True(contacts.TryCentroid(out var x, out _));
        Assert.Equal(800.0 / 3, x, 6);
    }

    [Fact]
    public void Tracking_allocates_nothing()
    {
        var contacts = WithThree();
        for (var i = 0; i < 100; i++)
        {
            contacts.Motion(i % 3, 100 + i, 100, out _, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            contacts.Motion(i % 3, 100 + (i % 50), 100, out _, out _);
            contacts.TryCentroid(out _, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
