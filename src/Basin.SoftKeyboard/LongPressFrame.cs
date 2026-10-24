namespace Basin.SoftKeyboard;

/// <summary>
/// The geometry of an open long-press list, in keyboard coordinates. The engine hit-tests it, and a view draws it.
/// </summary>
/// <param name="Frame">The bounds of the whole frame, clamped inside the keyboard width.</param>
/// <param name="PointerX">The horizontal center of the key that opened the list, where the frame's pointer goes.</param>
/// <param name="Lines">The number of entry lines, 1 or 2.</param>
/// <param name="LineLength">The number of cells in each line.</param>
/// <param name="Count">The number of entries.</param>
public readonly record struct LongPressFrame(KeyRect Frame, float PointerX, int Lines, int LineLength, int Count)
{
    /// <summary>
    /// The width of one entry cell.
    /// </summary>
    public const float CellWidth = 80;
    /// <summary>
    /// The height of one entry cell.
    /// </summary>
    public const float CellHeight = 60;
    /// <summary>
    /// The frame's margin to the left of the first cell.
    /// </summary>
    public const float LeftSide = 11;
    /// <summary>
    /// The frame's margin to the right of the last cell.
    /// </summary>
    public const float RightSide = 10;
    /// <summary>
    /// The frame's margin above the first line. The frame also overlaps the key top by this distance.
    /// </summary>
    public const float TopToKey = 10;
    /// <summary>
    /// The width of the pointer that a view draws under the frame. The geometry does not use it.
    /// </summary>
    public const float PointerWidth = 25;
    /// <summary>
    /// The largest number of entries that fit on one line. A longer list takes two lines.
    /// </summary>
    public const int SingleLineMax = 5;
    /// <summary>
    /// The frame height for one line.
    /// </summary>
    public const float OneLineHeight = 90;
    /// <summary>
    /// The frame height for two lines.
    /// </summary>
    public const float TwoLineHeight = 150;

    /// <summary>
    /// The value that <see cref="EntryAt"/> returns for a point outside the frame.
    /// </summary>
    public const int Outside = -2;
    /// <summary>
    /// The value that <see cref="EntryAt"/> returns for a point inside the frame but on no entry.
    /// </summary>
    public const int Nothing = -1;

    /// <summary>
    /// Places the list for a key. The frame is centered on the key where it fits, and its bottom overlaps the key top.
    /// </summary>
    /// <param name="key">The bounds of the key that opened the list.</param>
    /// <param name="count">The number of entries.</param>
    /// <param name="keyboardWidth">The keyboard width that the frame must stay inside.</param>
    /// <returns>The frame geometry.</returns>
    public static LongPressFrame For(in KeyRect key, int count, float keyboardWidth)
    {
        var lines = count > SingleLineMax ? 2 : 1;
        var lineLength = (count + lines - 1) / lines;
        var height = lines > 1 ? TwoLineHeight : OneLineHeight;
        var pointer = key.X + (key.Width / 2);
        var width = LeftSide + RightSide + (lineLength * CellWidth);
        var left = pointer - (CellWidth / 2) - LeftSide;
        if (left < 0)
        {
            left = 0;
        }
        else if (left + width > keyboardWidth)
        {
            left = keyboardWidth - width;
        }

        return new LongPressFrame(new KeyRect(left, key.Y - height + TopToKey, width, height), pointer, lines, lineLength, count);
    }

    /// <summary>
    /// Gives the bounds of one entry cell.
    /// </summary>
    /// <param name="index">The entry index, from 0 to <see cref="Count"/> minus 1.</param>
    /// <returns>The cell bounds in keyboard coordinates.</returns>
    public KeyRect CellRect(int index)
    {
        var line = index / LineLength;
        var column = index % LineLength;
        return new KeyRect(Frame.X + LeftSide + (column * CellWidth), Frame.Y + TopToKey + (line * CellHeight), CellWidth, CellHeight);
    }

    /// <summary>
    /// Finds the entry at a point.
    /// </summary>
    /// <param name="x">The horizontal position in keyboard coordinates.</param>
    /// <param name="y">The vertical position in keyboard coordinates.</param>
    /// <returns>The entry index, <see cref="Nothing"/> inside the frame on no entry, or <see cref="Outside"/> outside the frame.</returns>
    public int EntryAt(float x, float y)
    {
        if (x < Frame.X || x > Frame.Right || y < Frame.Y || y > Frame.Bottom)
        {
            return Outside;
        }

        var column = Math.Min((int)((x - Frame.X - LeftSide) / CellWidth), LineLength - 1);
        var line = (int)((y - Frame.Y - TopToKey) / CellHeight);
        var index = line == 0 ? column : column + LineLength;
        return index >= 0 && index < Count ? index : Nothing;
    }
}
