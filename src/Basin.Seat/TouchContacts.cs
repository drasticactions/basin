namespace Basin.Seat;

public sealed class TouchContacts
{
    public const int Capacity = 10;

    private readonly Contact[] _contacts = new Contact[Capacity];
    private int _count;
    private double _baseX;
    private double _baseY;

    public int Count => _count;

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
