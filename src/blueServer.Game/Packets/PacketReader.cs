using System.Buffers.Binary;
using System.Text;

namespace blueServer.Game.Packets;

public class PacketReader
{
    public const int HeaderSize = 4;

    private readonly byte[] _buffer;
    private int _position;

    public ushort Size { get; }
    public Opcode Opcode { get; }

    public PacketReader(byte[] buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            throw new PacketProtocolException(
                $"Packet must contain at least {HeaderSize} bytes for size and opcode.");
        }

        _buffer = buffer;

        Size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0, 2));
        if (Size != buffer.Length)
        {
            throw new PacketProtocolException(
                $"Packet size mismatch. Header size is {Size}, but actual size is {buffer.Length}.");
        }

        Opcode = (Opcode)BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2, 2));
        _position = 4;
    }

    public bool ReadBool()
    {
        EnsureAvailable(1);
        return _buffer[_position++] == 1;
    }

    public ushort ReadUShort()
    {
        EnsureAvailable(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_position, 2));
        _position += 2;
        return value;
    }

    public int ReadInt()
    {
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_position, 4));
        _position += 4;
        return value;
    }

    public long ReadLong()
    {
        EnsureAvailable(8);
        var value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.AsSpan(_position, 8));
        _position += 8;
        return value;
    }

    public string ReadString()
    {
        var length = ReadUShort();
        EnsureAvailable(length);

        var text = Encoding.UTF8.GetString(
                _buffer,
                _position,
                length);

        _position += length;
        return text;
    }

    private void EnsureAvailable(int byteCount)
    {
        if (_position + byteCount > _buffer.Length)
        {
            throw new PacketProtocolException(
                $"Packet payload is too short. Position={_position}, Requested={byteCount}, Length={_buffer.Length}.");
        }
    }
}
