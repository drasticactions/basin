namespace Basin.Seat;

/// <summary>
/// Tracks the live touch contacts and the motion of their center, with no allocation per event.
/// </summary>
public sealed class TouchContacts
{
    /// <summary>
    /// The largest number of contacts tracked. A down past it is dropped.
    /// </summary>
    public const int Capacity = 10;

    private readonly Contact[] _contacts = new Contact[Capacity];
    private int _count;
    private double _baseX;
    private double _baseY;

    /// <summary>
    /// Gets the number of live contacts.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Adds a contact, or moves it when the id is already live. The center motion restarts from the new center.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    public void Down(int id, double x, double y)
    {
        var slot = IndexOf(id);
        if (slot < 0)
        {
            slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }

            _contacts[slot].Id = id;
            _contacts[slot].Live = true;
            _count++;
        }

        _contacts[slot].X = x;
        _contacts[slot].Y = y;
        Rebase();
    }

    /// <summary>
    /// Moves a contact and gives the move of the center of all contacts.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The new horizontal position.</param>
    /// <param name="y">The new vertical position.</param>
    /// <param name="dx">The horizontal move of the center since the previous event.</param>
    /// <param name="dy">The vertical move of the center since the previous event.</param>
    /// <returns><see langword="false"/> when the contact is not live.</returns>
    public bool Motion(int id, double x, double y, out double dx, out double dy)
    {
        var slot = IndexOf(id);
        if (slot < 0)
        {
            dx = 0;
            dy = 0;
            return false;
        }

        _contacts[slot].X = x;
        _contacts[slot].Y = y;
        Centroid(out var centerX, out var centerY);
        dx = centerX - _baseX;
        dy = centerY - _baseY;
        _baseX = centerX;
        _baseY = centerY;
        return true;
    }

    /// <summary>
    /// Removes a contact. The center motion restarts from the new center.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <returns><see langword="false"/> when the contact is not live.</returns>
    public bool Up(int id)
    {
        var slot = IndexOf(id);
        if (slot < 0)
        {
            return false;
        }

        _contacts[slot].Live = false;
        _count--;
        Rebase();
        return true;
    }

    /// <summary>
    /// Gets the center of the live contacts.
    /// </summary>
    /// <param name="x">The horizontal center.</param>
    /// <param name="y">The vertical center.</param>
    /// <returns><see langword="false"/> when no contact is live.</returns>
    public bool TryCentroid(out double x, out double y)
    {
        if (_count == 0)
        {
            x = 0;
            y = 0;
            return false;
        }

        Centroid(out x, out y);
        return true;
    }

    /// <summary>
    /// Removes every contact.
    /// </summary>
    public void Clear()
    {
        for (var i = 0; i < _contacts.Length; i++)
        {
            _contacts[i].Live = false;
        }

        _count = 0;
        _baseX = 0;
        _baseY = 0;
    }

    private int IndexOf(int id)
    {
        for (var i = 0; i < _contacts.Length; i++)
        {
            if (_contacts[i].Live && _contacts[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private int FreeSlot()
    {
        for (var i = 0; i < _contacts.Length; i++)
        {
            if (!_contacts[i].Live)
            {
                return i;
            }
        }

        return -1;
    }

    private void Centroid(out double x, out double y)
    {
        double sumX = 0;
        double sumY = 0;
        for (var i = 0; i < _contacts.Length; i++)
        {
            if (_contacts[i].Live)
            {
                sumX += _contacts[i].X;
                sumY += _contacts[i].Y;
            }
        }

        x = sumX / _count;
        y = sumY / _count;
    }

    private void Rebase()
    {
        if (_count == 0)
        {
            _baseX = 0;
            _baseY = 0;
            return;
        }

        Centroid(out _baseX, out _baseY);
    }

    private struct Contact
    {
        public int Id;
        public double X;
        public double Y;
        public bool Live;
    }
}
