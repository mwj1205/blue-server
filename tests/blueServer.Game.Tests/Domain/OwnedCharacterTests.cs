using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class OwnedCharacterTests
{
    [Fact]
    public void Create_SetsInitialGrowthFromTemplate()
    {
        var template = new CharacterTemplate
        {
            Id = 10,
            Name = "Test Character",
            Rarity = 3,
            Role = "Dealer"
        };

        var character = OwnedCharacter.Create(1, template);

        Assert.Equal(1, character.PlayerId);
        Assert.Equal(template.Id, character.CharacterTemplateId);
        Assert.Equal(OwnedCharacter.InitialLevel, character.Level);
        Assert.Equal(template.Rarity, character.Star);
        Assert.Equal(OwnedCharacter.InitialExp, character.Exp);
    }
}
