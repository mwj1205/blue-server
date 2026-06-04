namespace blueServer.Game.Packets;

public enum Opcode : ushort
{
    Login = 1,

    Chat = 2,

    Ping = 3,
    Pong = 4,

    ChatMessage = 1000  // S -> C
}
