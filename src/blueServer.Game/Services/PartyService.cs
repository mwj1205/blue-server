using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Services;

public sealed class PartyService
{
    private const int MaxPartyNameLength = 20;

    private readonly GameDbContext _db;

    public PartyService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<PartyResult> GetAsync(
        long playerId,
        int partyNo,
        CancellationToken cancellationToken)
    {
        var validationMessage = ValidatePartyNo(partyNo);

        if (validationMessage is not null)
        {
            return PartyResult.Fail(validationMessage, partyNo);
        }

        var party = await QueryParty(playerId, partyNo)
            .FirstOrDefaultAsync(cancellationToken);

        return party ?? PartyResult.Fail("Party not found", partyNo);
    }

    public async Task<PartyResult> SaveAsync(
        long playerId,
        int partyNo,
        string name,
        IReadOnlyList<PartySaveSlot> slots,
        CancellationToken cancellationToken)
    {
        var validationMessage = ValidateSaveRequest(partyNo, name, slots);

        if (validationMessage is not null)
        {
            return PartyResult.Fail(validationMessage, partyNo);
        }

        var playerExists = await _db.Players
            .AsNoTracking()
            .AnyAsync(
                player => player.Id == playerId,
                cancellationToken);

        if (!playerExists)
        {
            return PartyResult.Fail("Player not found", partyNo);
        }

        var ownedCharacterIds = slots
            .Select(slot => slot.OwnedCharacterId)
            .Distinct()
            .ToArray();

        var ownedCharacters = await _db.OwnedCharacters
            .Where(character =>
                character.PlayerId == playerId &&
                ownedCharacterIds.Contains(character.Id))
            .ToDictionaryAsync(
                character => character.Id,
                cancellationToken);

        if (ownedCharacters.Count != ownedCharacterIds.Length)
        {
            return PartyResult.Fail(
                "Some owned characters do not belong to player",
                partyNo);
        }

        var party = await _db.Parties
            .Include(x => x.Slots)
            .FirstOrDefaultAsync(
                x => x.PlayerId == playerId &&
                    x.PartyNo == partyNo,
                cancellationToken);

        if (party is null)
        {
            party = Party.Create(playerId, partyNo, name);
            _db.Parties.Add(party);
        }
        else
        {
            party.Name = string.IsNullOrWhiteSpace(name)
                ? $"Party {partyNo}"
                : name.Trim();

            var existingSlots = party.Slots.ToArray();
            _db.PartySlots.RemoveRange(existingSlots);
            party.Slots.Clear();
        }

        foreach (var slot in slots.OrderBy(slot => slot.SlotIndex))
        {
            party.SetSlot(
                slot.SlotIndex,
                ownedCharacters[slot.OwnedCharacterId]);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(playerId, partyNo, cancellationToken);
    }

    private IQueryable<PartyResult> QueryParty(
        long playerId,
        int partyNo)
    {
        return _db.Parties
            .AsNoTracking()
            .Where(party =>
                party.PlayerId == playerId &&
                party.PartyNo == partyNo)
            .Select(party => new PartyResult(
                true,
                "Party loaded",
                party.PartyNo,
                party.Name,
                party.Slots
                    .OrderBy(slot => slot.SlotIndex)
                    .Select(slot => new PartySlotResult(
                        slot.SlotIndex,
                        slot.OwnedCharacterId,
                        slot.OwnedCharacter!.CharacterTemplateId,
                        slot.OwnedCharacter.CharacterTemplate!.Name,
                        slot.OwnedCharacter.CharacterTemplate.Rarity,
                        slot.OwnedCharacter.CharacterTemplate.Role,
                        slot.OwnedCharacter.Level,
                        slot.OwnedCharacter.Star,
                        slot.OwnedCharacter.Exp))
                    .ToList()));
    }

    private static string? ValidateSaveRequest(
        int partyNo,
        string name,
        IReadOnlyList<PartySaveSlot> slots)
    {
        var partyNoValidationMessage = ValidatePartyNo(partyNo);

        if (partyNoValidationMessage is not null)
        {
            return partyNoValidationMessage;
        }

        if (name.Length > MaxPartyNameLength)
        {
            return $"Party name must be at most {MaxPartyNameLength} characters";
        }

        if (slots.Count > PartySlot.MaxSlotIndex)
        {
            return $"Slots must be at most {PartySlot.MaxSlotIndex}";
        }

        if (slots.Any(slot =>
            slot.SlotIndex is < PartySlot.MinSlotIndex or > PartySlot.MaxSlotIndex))
        {
            return $"Slot index must be between {PartySlot.MinSlotIndex} and {PartySlot.MaxSlotIndex}";
        }

        if (slots.Any(slot => slot.OwnedCharacterId <= 0))
        {
            return "Owned character id must be greater than zero";
        }

        if (slots.Select(slot => slot.SlotIndex).Distinct().Count() != slots.Count)
        {
            return "Slot index must not be duplicated";
        }

        if (slots.Select(slot => slot.OwnedCharacterId).Distinct().Count() != slots.Count)
        {
            return "Owned character id must not be duplicated";
        }

        return null;
    }

    private static string? ValidatePartyNo(int partyNo)
    {
        if (partyNo is < Party.MinPartyNo or > Party.MaxPartyNo)
        {
            return $"Party no must be between {Party.MinPartyNo} and {Party.MaxPartyNo}";
        }

        return null;
    }
}

public sealed record PartySaveSlot(
    int SlotIndex,
    long OwnedCharacterId);

public sealed record PartyResult(
    bool IsSuccess,
    string Message,
    int PartyNo,
    string Name,
    IReadOnlyList<PartySlotResult> Slots)
{
    public static PartyResult Fail(
        string message,
        int partyNo)
    {
        return new PartyResult(
            false,
            message,
            partyNo,
            string.Empty,
            []);
    }
}

public sealed record PartySlotResult(
    int SlotIndex,
    long OwnedCharacterId,
    int CharacterTemplateId,
    string CharacterName,
    int Rarity,
    string Role,
    int Level,
    int Star,
    long Exp);
