namespace blueServer.Game;

public class ReceiveBuffer
{
    private readonly byte[] _buffer;
    private int _writePos;

    public ReceiveBuffer(int size)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Receive buffer size must be positive.");
        }

        _buffer = new byte[size];
    }

    public byte[] Buffer => _buffer;

    public int Length => _writePos;

    public int Capacity => _buffer.Length;

    public int RemainingCapacity => _buffer.Length - _writePos;

    public int Write(byte[] data, int length)
    {
        if (length < 0 || length > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length > RemainingCapacity)
        {
            throw new InvalidOperationException(
                $"Receive buffer capacity exceeded. Capacity={Capacity}, Length={Length}, Incoming={length}.");
        }

        data.AsSpan(0, length).CopyTo(_buffer.AsSpan(_writePos));
        _writePos += length;

        return _writePos;
    }

    public void Remove(int length)
    {
        if (length < 0 || length > _writePos)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0)
        {
            return;
        }

        _buffer.AsSpan(length, _writePos - length)
            .CopyTo(_buffer.AsSpan(0));

        _writePos -= length;
    }
}
