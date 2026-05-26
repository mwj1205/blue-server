using System.Text;
using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public static class PacketHandler
{
    public static void Handle(Packet packet)
    {
        switch (packet.Opcode)
        {
            case Opcode.Login:
                {
                    var text = Encoding.UTF8.GetString(packet.Payload);
                    Console.WriteLine($"[Login] {text}");
                    break;
                }

            case Opcode.Chat:
                {
                    var text = Encoding.UTF8.GetString(packet.Payload);
                    Console.WriteLine($"[Chat] {text}");
                    break;
                }

            default:
                {
                    Console.WriteLine($"Unknown Opcode: {packet.Opcode}");
                    break;
                }
        }
    }
}
