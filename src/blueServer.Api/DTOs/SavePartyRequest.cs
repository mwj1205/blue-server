namespace blueServer.Api.DTOs;

public class SavePartyRequest
{
    public string Name { get; set; } = "";
    public List<SavePartySlotRequest> Slots { get; set; } = [];
}

public class SavePartySlotRequest
{
    public int SlotIndex { get; set; }
    public long OwnedCharacterId { get; set; }
}
