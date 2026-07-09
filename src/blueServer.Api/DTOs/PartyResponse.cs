namespace blueServer.Api.DTOs;

public class PartyResponse
{
    public long Id { get; set; }
    public int PartyNo { get; set; }
    public string Name { get; set; } = "";
    public IReadOnlyList<PartySlotResponse> Slots { get; set; } = [];
}

public class PartySlotResponse
{
    public int SlotIndex { get; set; }
    public long OwnedCharacterId { get; set; }
    public int CharacterTemplateId { get; set; }
    public string CharacterName { get; set; } = "";
    public int Rarity { get; set; }
    public string Role { get; set; } = "";
    public int Level { get; set; }
    public int Star { get; set; }
    public long Exp { get; set; }
}
