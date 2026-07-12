using blueServer.Api.DTOs;
using blueServer.Api.Exceptions;
using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Api.Services;

public class PartyService
{
    private readonly GameDbContext _db;

    public PartyService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<PartyResponse?> GetPartyAsync(
        long playerId,
        int partyNo,
        CancellationToken cancellationToken)
    {
        return await _db.Parties
            .AsNoTracking()
            .Where(party =>
                party.PlayerId == playerId &&
                party.PartyNo == partyNo)
            .Select(party => new PartyResponse
            {
                Id = party.Id,
                PartyNo = party.PartyNo,
                Name = party.Name,
                Slots = party.Slots
                    .OrderBy(slot => slot.SlotIndex)
                    .Select(slot => new PartySlotResponse
                    {
                        SlotIndex = slot.SlotIndex,
                        OwnedCharacterId = slot.OwnedCharacterId,
                        CharacterTemplateId = slot.OwnedCharacter!.CharacterTemplateId,
                        CharacterName = slot.OwnedCharacter.CharacterTemplate!.Name,
                        Rarity = slot.OwnedCharacter.CharacterTemplate.Rarity,
                        Role = slot.OwnedCharacter.CharacterTemplate.Role,
                        Level = slot.OwnedCharacter.Level,
                        Star = slot.OwnedCharacter.Star,
                        Exp = slot.OwnedCharacter.Exp
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PartyResponse?> SavePartyAsync(
        long playerId,
        int partyNo,
        SavePartyRequest request,
        CancellationToken cancellationToken)
    {
        var playerExists = await _db.Players
            .AsNoTracking()
            .AnyAsync(
                player => player.Id == playerId,
                cancellationToken);

        if (!playerExists)
        {
            return null;
        }

        var ownedCharacterIds = request.Slots
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
            throw new GameException("Some owned characters do not belong to player");
        }

        var party = await _db.Parties
            .Include(x => x.Slots)
            .FirstOrDefaultAsync(
                x => x.PlayerId == playerId &&
                    x.PartyNo == partyNo,
                cancellationToken);

        if (party is null)
        {
            party = Party.Create(playerId, partyNo, request.Name);
            _db.Parties.Add(party);
        }
        else
        {
            party.Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"Party {partyNo}"
                : request.Name.Trim();

            _db.PartySlots.RemoveRange(party.Slots);
            party.Slots.Clear();
        }

        foreach (var slot in request.Slots.OrderBy(slot => slot.SlotIndex))
        {
            party.SetSlot(
                slot.SlotIndex,
                ownedCharacters[slot.OwnedCharacterId]);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await GetPartyAsync(playerId, partyNo, cancellationToken);
    }
}
