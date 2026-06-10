namespace blueServer.Domain.Entities;

public class OwnedCharacter
{
    public long Id { get; set; }

    public long PlayerId { get; set; }

    // 캐릭터 종류
    public int CharacterTemplateId { get; set; }

    // 육성 정보
    public int Level { get; set; }
    public int Star { get; set; }
    public long Exp { get; set; }

    // Navigation Property
    public Player? Player { get; set; }
    public CharacterTemplate? CharacterTemplate { get; set; }

}
