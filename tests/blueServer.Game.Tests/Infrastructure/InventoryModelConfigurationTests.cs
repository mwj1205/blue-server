using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class InventoryModelConfigurationTests
{
    [Fact]
    public void PlayerItem_UsesUniquePlayerAndTemplateIndex()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(PlayerItem));

        Assert.NotNull(entityType);
        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([
                        nameof(PlayerItem.PlayerId),
                        nameof(PlayerItem.ItemTemplateId)
                    ]));
    }

    [Fact]
    public void PlayerItem_UsesVersionAsConcurrencyToken()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(PlayerItem));
        var versionProperty = entityType?.FindProperty(nameof(PlayerItem.Version));

        Assert.NotNull(versionProperty);
        Assert.True(versionProperty.IsConcurrencyToken);
    }

    [Fact]
    public void PlayerItem_CascadesWithPlayerButRestrictsTemplateDeletion()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(PlayerItem));

        Assert.NotNull(entityType);

        var playerForeignKey = entityType.GetForeignKeys()
            .Single(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(Player));
        var templateForeignKey = entityType.GetForeignKeys()
            .Single(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(ItemTemplate));

        Assert.Equal(DeleteBehavior.Cascade, playerForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, templateForeignKey.DeleteBehavior);
    }

    [Fact]
    public void ItemTemplate_ConfiguresNameLengthAndIntegerItemType()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(ItemTemplate));

        Assert.NotNull(entityType);
        Assert.Equal(
            ItemTemplate.MaxNameLength,
            entityType.FindProperty(nameof(ItemTemplate.Name))?.GetMaxLength());
        Assert.Equal(
            typeof(int),
            entityType.FindProperty(nameof(ItemTemplate.Type))?.GetProviderClrType());
    }

    [Fact]
    public void ItemTemplate_DoesNotGenerateStableMasterDataId()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(ItemTemplate));
        var idProperty = entityType?.FindProperty(nameof(ItemTemplate.Id));

        Assert.NotNull(idProperty);
        Assert.Equal(ValueGenerated.Never, idProperty.ValueGenerated);
    }

    private static GameDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=blue_server_model_tests;" +
                "Username=postgres;Password=postgres")
            .Options;

        return new GameDbContext(options);
    }
}
