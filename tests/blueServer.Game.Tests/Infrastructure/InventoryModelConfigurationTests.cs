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
    public void ItemTemplate_ConfiguresCatalogFieldsAndIntegerItemType()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(ItemTemplate));

        Assert.NotNull(entityType);
        Assert.Equal(
            ItemTemplate.MaxCodeLength,
            entityType.FindProperty(nameof(ItemTemplate.Code))?.GetMaxLength());
        Assert.Equal(
            ItemTemplate.MaxLocalizationKeyLength,
            entityType.FindProperty(nameof(ItemTemplate.NameKey))?.GetMaxLength());
        Assert.Equal(
            ItemTemplate.MaxLocalizationKeyLength,
            entityType.FindProperty(nameof(ItemTemplate.DescriptionKey))?.GetMaxLength());
        Assert.Equal(
            typeof(int),
            entityType.FindProperty(nameof(ItemTemplate.Type))?.GetProviderClrType());
        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(ItemTemplate.Code)]));
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

    [Fact]
    public void MailItemAttachment_UsesUniqueMailAndTemplateIndex()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(
            typeof(MailItemAttachment));

        Assert.NotNull(entityType);
        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([
                        nameof(MailItemAttachment.MailId),
                        nameof(MailItemAttachment.ItemTemplateId)
                    ]));
    }

    [Fact]
    public void MailItemAttachment_CascadesWithMailButRestrictsTemplateDeletion()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(
            typeof(MailItemAttachment));

        Assert.NotNull(entityType);

        var mailForeignKey = entityType.GetForeignKeys()
            .Single(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(Mail));
        var templateForeignKey = entityType.GetForeignKeys()
            .Single(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(ItemTemplate));

        Assert.Equal(DeleteBehavior.Cascade, mailForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, templateForeignKey.DeleteBehavior);
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
