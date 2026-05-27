using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public static class PacketHandler
{
    public static void Handle(PacketReader reader)
    {
        switch (reader.Opcode)
        {
            case Opcode.Login:
                {

                    var nickname = reader.ReadString();
                    Console.WriteLine($"[Login] {nickname}");
                    break;
                }

            case Opcode.Chat:
                {
                    var message = reader.ReadString();
                    Console.WriteLine($"[Chat] {message}");
                    break;
                }

            default:
                {
                    Console.WriteLine($"Unknown Opcode: {reader.Opcode}");
                    break;
                }
        }
    }
}
