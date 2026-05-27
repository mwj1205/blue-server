namespace blueServer.Game.Packets;

public class LoginPacket
{
    public string Nickname { get; set; } = "";

    public byte[] Serialize()
    {
        var writer = new PacketWriter();
        writer.WriteUShort((ushort)Opcode.Login);
        writer.WriteString(Nickname);
        var body = writer.ToArray();

        return [
            .. BitConverter.GetBytes((ushort)(body.Length + 2)),
            .. body
        ];
    }
}
