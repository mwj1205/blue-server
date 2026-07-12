namespace blueServer.Domain.Entities;

public class Party
{
    public const int MinPartyNo = 1;
    public const int MaxPartyNo = 5;

    public long Id { get; set; }
    public long PlayerId { get; set; }
    public int PartyNo { get; set; }
    public string Name { get; set; } = string.Empty;

    public Player? Player { get; set; }
    public ICollection<PartySlot> Slots { get; set; } = new List<PartySlot>();

    public static Party Create(
        long playerId,
        int partyNo,
        string name = "")
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        ValidatePartyNo(partyNo);

        return new Party
        {
            PlayerId = playerId,
            PartyNo = partyNo,
            Name = string.IsNullOrWhiteSpace(name)
                ? $"Party {partyNo}"
                : name.Trim()
        };
    }

    public void SetSlot(
        int slotIndex,
        OwnedCharacter ownedCharacter)
    {
        ArgumentNullException.ThrowIfNull(ownedCharacter);
        PartySlot.ValidateSlotIndex(slotIndex);

        if (ownedCharacter.Id <= 0)
        {
            throw new ArgumentException(
                "Owned character must be persisted before party assignment.",
                nameof(ownedCharacter));
        }

        if (ownedCharacter.PlayerId != PlayerId)
        {
            throw new InvalidOperationException(
                "Cannot assign another player's character to this party.");
        }

        var duplicatedSlot = Slots.FirstOrDefault(slot =>
            slot.SlotIndex != slotIndex &&
            slot.OwnedCharacterId == ownedCharacter.Id);

        if (duplicatedSlot is not null)
        {
            throw new InvalidOperationException(
                "Cannot assign the same character to multiple party slots.");
        }

        var slot = Slots.FirstOrDefault(slot =>
            slot.SlotIndex == slotIndex);

        if (slot is null)
        {
            Slots.Add(PartySlot.Create(slotIndex, ownedCharacter));
            return;
        }

        slot.Assign(ownedCharacter);
    }

    public void ClearSlot(int slotIndex)
    {
        PartySlot.ValidateSlotIndex(slotIndex);

        var slot = Slots.FirstOrDefault(slot =>
            slot.SlotIndex == slotIndex);

        if (slot is null)
        {
            return;
        }

        Slots.Remove(slot);
    }

    private static void ValidatePartyNo(int partyNo)
    {
        if (partyNo is < MinPartyNo or > MaxPartyNo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partyNo),
                partyNo,
                $"Party no must be between {MinPartyNo} and {MaxPartyNo}.");
        }
    }
}
