namespace blueServer.Game.Packets;

public class ChatMessagePacket
{
    public string Message { get; set; } = "";

    public byte[] Serialize()
    {
        var writer = new PacketWriter();

        writer.WriteUShort((ushort)Opcode.ChatMessage);
        writer.WriteString(Message);

        var body = writer.ToArray();

        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}