namespace BlueServer.Client.Protocol
{
    public enum GameOpcode : ushort
    {
        Login = 1,
        LoginResult = 2,
        PlayerProfile = 15,
        PlayerProfileResult = 16,
        MailList = 17,
        MailListResult = 18,
        MailDetail = 19,
        MailDetailResult = 20,
        MailClaim = 21,
        MailClaimResult = 22,
        MailClaimAll = 23,
        MailClaimAllResult = 24,
        MailRead = 25,
        MailReadResult = 26
    }
}
