using System.Text.Json;
using System.Text.Json.Serialization;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;

namespace blueServer.Infrastructure.GameData;

public static class ItemCatalogJsonLoader
{
    public const int SupportedSchemaVersion = 1;
    public const int MaxCodeLength = 64;
    public const int MaxLocalizationKeyLength = 150;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: false)
        }
    };

    public static async Task<ItemCatalog> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ItemCatalogDocument? document;

        try
        {
            document = await JsonSerializer.DeserializeAsync<ItemCatalogDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Item catalog is not valid JSON.",
                exception);
        }

        if (document is null)
        {
            throw new InvalidDataException("Item catalog is empty.");
        }

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Item catalog schema version {document.SchemaVersion} is not supported. " +
                $"Expected {SupportedSchemaVersion}.");
        }

        if (document.Items is not { Count: > 0 })
        {
            throw new InvalidDataException(
                "Item catalog must contain at least one item.");
        }

        var ids = new HashSet<int>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new List<ItemTemplateDefinition>(document.Items.Count);

        foreach (var item in document.Items)
        {
            if (!ids.Add(item.Id))
            {
                throw new InvalidDataException(
                    $"Item catalog contains duplicate id {item.Id}.");
            }

            if (item.Id <= 0)
            {
                throw CreateEntryException(item.Id, "id must be greater than zero");
            }

            var code = NormalizeRequiredValue(
                item.Id,
                item.Code,
                "code",
                MaxCodeLength);

            if (!codes.Add(code))
            {
                throw new InvalidDataException(
                    $"Item catalog contains duplicate code '{code}'.");
            }

            var nameKey = NormalizeRequiredValue(
                item.Id,
                item.NameKey,
                "nameKey",
                MaxLocalizationKeyLength);
            var descriptionKey = NormalizeRequiredValue(
                item.Id,
                item.DescriptionKey,
                "descriptionKey",
                MaxLocalizationKeyLength);

            if (!Enum.IsDefined(item.Type))
            {
                throw CreateEntryException(item.Id, "type is not supported");
            }

            definitions.Add(new ItemTemplateDefinition(
                item.Id,
                code,
                nameKey,
                descriptionKey,
                item.Type,
                ItemTemplate.InventoryMaxQuantity));
        }

        return new ItemCatalog(
            document.SchemaVersion,
            definitions.AsReadOnly());
    }

    private static string NormalizeRequiredValue(
        int itemId,
        string? value,
        string propertyName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw CreateEntryException(itemId, $"{propertyName} is required");
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw CreateEntryException(
                itemId,
                $"{propertyName} must not exceed {maxLength} characters");
        }

        return normalized;
    }

    private static InvalidDataException CreateEntryException(
        int itemId,
        string detail)
    {
        return new InvalidDataException(
            $"Item catalog entry {itemId} is invalid: {detail}.");
    }

    private sealed class ItemCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<ItemDocument>? Items { get; init; }
    }

    private sealed class ItemDocument
    {
        public int Id { get; init; }
        public string? Code { get; init; }
        public string? NameKey { get; init; }
        public string? DescriptionKey { get; init; }
        public ItemType Type { get; init; }
    }
}
