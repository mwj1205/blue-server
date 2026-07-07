namespace blueServer.Game.Packets;

public sealed class LoginRequestPacket
{
    public string AccessToken { get; init; } = string.Empty;

    public static LoginRequestPacket Read(PacketReader reader)
    {
        return new LoginRequestPacket
        {
            AccessToken = reader.ReadString()
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.Login);
        bodyWriter.WriteString(AccessToken);

        var body = bodyWriter.ToArray();

        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
