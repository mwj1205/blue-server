namespace blueServer.Game.Packets;

public enum Opcode : ushort
{
    Login = 1,
    LoginResult = 2,

    Chat = 3,

    Ping = 4,
    Pong = 5,

    ChatMessage = 1000  // S -> C
}
