namespace blueServer.Domain.Entities;

public class OwnedCharacter
{
    public const int InitialLevel = 1;
    public const long InitialExp = 0;

    public long Id { get; set; }
    public long PlayerId { get; set; }

    // 캐릭터 종류
    public int CharacterTemplateId { get; set; }

    // 성장 정보
    public int Level { get; set; }
    public int Star { get; set; }
    public long Exp { get; set; }
    public uint Version { get; set; }

    // EF Core 관계 탐색
    public Player? Player { get; set; }
    public CharacterTemplate? CharacterTemplate { get; set; }

    public static OwnedCharacter Create(
        long playerId,
        CharacterTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        return new OwnedCharacter
        {
            PlayerId = playerId,
            CharacterTemplateId = template.Id,
            Level = InitialLevel,
            Star = template.Rarity,
            Exp = InitialExp
        };
    }
}
