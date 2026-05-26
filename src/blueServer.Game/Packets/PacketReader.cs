using System.Buffers.Binary;

namespace blueServer.Game.Packets;

public static class PacketReader
{
    public static Packet Read(byte[] buffer, int length)
    {
        var size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0, 2));

        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2, 2));

        var payload = buffer.AsSpan(4, length - 4).ToArray();

        return new Packet
        {
            Size = size,
            Opcode = (Opcode)opcode,
            Payload = payload
        };
    }
}
