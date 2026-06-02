namespace blueServer.Game.Packets;

public class ChatMessagePacket
{
    public string Message { get; set; } = "";

    public byte[] Serialize()
    {
        // body 데이터 먼저 직렬화
        var writer = new PacketWriter();
        writer.WriteUShort((ushort)Opcode.ChatMessage);
        writer.WriteString(Message);

        var body = writer.ToArray();

        // 최종 패킷 조립 (헤더 결합)
        var finalWriter = new PacketWriter();
        // 헤더 크기(2바이트) + body 크기
        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
