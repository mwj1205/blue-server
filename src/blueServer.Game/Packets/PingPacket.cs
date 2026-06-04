namespace blueServer.Game.Packets;

public class PingPacket
{
    public byte[] Serialize()
    {
        var writer = new PacketWriter();

        writer.WriteUShort((ushort)Opcode.Ping);
        var body = writer.ToArray();

        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
