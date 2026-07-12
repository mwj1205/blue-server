namespace BlueServer.Client.Models
{
    public sealed class PlayerProfileResponse
    {
        public PlayerProfileResponse(
            bool success,
            string message,
            long playerId,
            string nickname,
            int gold,
            int gem,
            int ownedCharacterCount,
            int partyCount,
            int clearedStageCount,
            int totalStageClearCount)
        {
            Success = success;
            Message = message;
            PlayerId = playerId;
            Nickname = nickname;
            Gold = gold;
            Gem = gem;
            OwnedCharacterCount = ownedCharacterCount;
            PartyCount = partyCount;
            ClearedStageCount = clearedStageCount;
            TotalStageClearCount = totalStageClearCount;
        }

        public bool Success { get; private set; }
        public string Message { get; private set; }
        public long PlayerId { get; private set; }
        public string Nickname { get; private set; }
        public int Gold { get; private set; }
        public int Gem { get; private set; }
        public int OwnedCharacterCount { get; private set; }
        public int PartyCount { get; private set; }
        public int ClearedStageCount { get; private set; }
        public int TotalStageClearCount { get; private set; }
    }
}
