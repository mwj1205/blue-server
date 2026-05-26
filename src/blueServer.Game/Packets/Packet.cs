namespace blueServer.Game.Packets;

public class Packet
{
    public ushort Size { get; set; }
    public Opcode Opcode { get; set; }
    public byte[] Payload { get; set; } = [];
}
