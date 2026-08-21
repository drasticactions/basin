namespace Basin;

public readonly record struct BufferDataView(nint Data, int Stride, DrmFormat Format);
