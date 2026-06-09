namespace blueServer.Game.Packets;

public class LoginResultPacket
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.LoginResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);

        var body = bodyWriter.ToArray();

        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
