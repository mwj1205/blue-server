using System.Buffers.Binary;
using System.Text;

namespace blueServer.Game.Packets;

public class PacketReader
{
    private readonly byte[] _buffer;
    private int _position;

    public ushort Size { get; }
    public Opcode Opcode { get; }

    public PacketReader(byte[] buffer)
    {
        _buffer = buffer;

        Size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0, 2));
        Opcode = (Opcode)BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2, 2));
        _position = 4;
    }

    public ushort ReadUShort()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_position, 2));
        _position += 2;
        return value;
    }

    public string ReadString()
    {
        var length = ReadUShort();

        var text = Encoding.UTF8.GetString(
                _buffer,
                _position,
                length);

        _position += length;
        return text;
    }
}
