using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class PartyTests
{
    [Fact]
    public void Create_SetsPlayerAndPartyNo()
    {
        var party = Party.Create(1, 2, "Main");

        Assert.Equal(1, party.PlayerId);
        Assert.Equal(2, party.PartyNo);
        Assert.Equal("Main", party.Name);
        Assert.Empty(party.Slots);
    }

    [Fact]
    public void SetSlot_AddsCharacter_WhenCharacterBelongsToPlayer()
    {
        var party = Party.Create(1, 1);
        var character = CreateOwnedCharacter(100, playerId: 1);

        party.SetSlot(1, character);

        var slot = Assert.Single(party.Slots);
        Assert.Equal(1, slot.SlotIndex);
        Assert.Equal(character.Id, slot.OwnedCharacterId);
    }

    [Fact]
    public void SetSlot_Throws_WhenCharacterBelongsToAnotherPlayer()
    {
        var party = Party.Create(1, 1);
        var character = CreateOwnedCharacter(100, playerId: 2);

        Assert.Throws<InvalidOperationException>(
            () => party.SetSlot(1, character));
    }

    [Fact]
    public void SetSlot_Throws_WhenSameCharacterIsAssignedTwice()
    {
        var party = Party.Create(1, 1);
        var character = CreateOwnedCharacter(100, playerId: 1);

        party.SetSlot(1, character);

        Assert.Throws<InvalidOperationException>(
            () => party.SetSlot(2, character));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void SetSlot_Throws_WhenSlotIndexIsOutOfRange(int slotIndex)
    {
        var party = Party.Create(1, 1);
        var character = CreateOwnedCharacter(100, playerId: 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => party.SetSlot(slotIndex, character));
    }

    private static OwnedCharacter CreateOwnedCharacter(
        long id,
        long playerId)
    {
        return new OwnedCharacter
        {
            Id = id,
            PlayerId = playerId,
            CharacterTemplateId = 10,
            Level = OwnedCharacter.InitialLevel,
            Star = 3,
            Exp = OwnedCharacter.InitialExp
        };
    }
}
