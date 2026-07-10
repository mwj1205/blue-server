namespace blueServer.Game.Packets;

public enum Opcode : ushort
{
    Login = 1,
    LoginResult = 2,

    Chat = 3,

    Ping = 4,
    Pong = 5,

    CharacterGacha = 6,
    CharacterGachaResult = 7,

    OwnedCharacterList = 8,
    OwnedCharacterListResult = 9,

    PartyGet = 10,
    PartySave = 11,
    PartyResult = 12,

    ChatMessage = 1000  // S -> C
}
