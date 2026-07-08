namespace blueServer.Game.Packets;

public sealed class OwnedCharacterListRequestPacket
{
    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.OwnedCharacterList);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
