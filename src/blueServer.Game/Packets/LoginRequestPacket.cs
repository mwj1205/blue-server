namespace blueServer.Game.Packets;

public class LoginRequestPacket
{
    public string Nickname { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}