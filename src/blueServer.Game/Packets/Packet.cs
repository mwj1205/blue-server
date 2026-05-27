namespace blueServer.Game.Packets;

public class Packet
{
    public ushort Size { get; set; }   // 패킷 전체 크기 (헤더 4바이트 포함)
    public Opcode Opcode { get; set; }  // 패킷 종류 식별자 (Opcode 값)
    public byte[] Payload { get; set; } = [];  // 실제 데이터
}
