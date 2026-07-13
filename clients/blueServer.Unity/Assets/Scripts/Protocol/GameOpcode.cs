namespace BlueServer.Client.Protocol
{
    public enum GameOpcode : ushort
    {
        Login = 1,
        LoginResult = 2,
        PlayerProfile = 15,
        PlayerProfileResult = 16
    }
}
