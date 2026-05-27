using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public static class PacketHandler
{
    public static async Task Handle(PacketReader reader)
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

                    var response = System.Text.Encoding.UTF8.GetBytes($"Broadcast: {message}");

                    await SessionManager.BroadcastAsync(response);

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
