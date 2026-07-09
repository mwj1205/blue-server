namespace blueServer.Domain.Entities;

public class PartySlot
{
    public const int MinSlotIndex = 1;
    public const int MaxSlotIndex = 6;

    public long Id { get; set; }
    public long PartyId { get; set; }
    public int SlotIndex { get; set; }
    public long OwnedCharacterId { get; set; }

    public Party? Party { get; set; }
    public OwnedCharacter? OwnedCharacter { get; set; }

    public static PartySlot Create(
        int slotIndex,
        OwnedCharacter ownedCharacter)
    {
        ArgumentNullException.ThrowIfNull(ownedCharacter);
        ValidateSlotIndex(slotIndex);

        if (ownedCharacter.Id <= 0)
        {
            throw new ArgumentException(
                "Owned character must be persisted before party assignment.",
                nameof(ownedCharacter));
        }

        return new PartySlot
        {
            SlotIndex = slotIndex,
            OwnedCharacterId = ownedCharacter.Id,
            OwnedCharacter = ownedCharacter
        };
    }

    public void Assign(OwnedCharacter ownedCharacter)
    {
        ArgumentNullException.ThrowIfNull(ownedCharacter);

        if (ownedCharacter.Id <= 0)
        {
            throw new ArgumentException(
                "Owned character must be persisted before party assignment.",
                nameof(ownedCharacter));
        }

        OwnedCharacterId = ownedCharacter.Id;
        OwnedCharacter = ownedCharacter;
    }

    public static void ValidateSlotIndex(int slotIndex)
    {
        if (slotIndex is < MinSlotIndex or > MaxSlotIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex),
                slotIndex,
                $"Slot index must be between {MinSlotIndex} and {MaxSlotIndex}.");
        }
    }
}
