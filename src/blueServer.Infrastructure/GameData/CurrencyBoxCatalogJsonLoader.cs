using System.Text.Json;
using System.Text.Json.Serialization;
using blueServer.Domain.Currencies;

namespace blueServer.Infrastructure.GameData;

public static class CurrencyBoxCatalogJsonLoader
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static async Task<CurrencyBoxCatalog> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        BoxCatalogDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<BoxCatalogDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Currency box catalog is not valid JSON.", exception);
        }

        if (document is null || document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException("Currency box catalog is empty or its schema version is unsupported.");
        }

        if (document.Boxes is not { Count: > 0 })
        {
            throw new InvalidDataException("Currency box catalog must contain at least one box.");
        }

        var ids = new HashSet<int>();
        var definitions = new List<CurrencyBoxDefinition>(document.Boxes.Count);
        foreach (var box in document.Boxes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (box is null || box.ItemTemplateId <= 0 || !ids.Add(box.ItemTemplateId))
            {
                throw new InvalidDataException("Currency box ids must be positive and unique.");
            }

            if (!Enum.IsDefined(box.CurrencyType) || !Enum.IsDefined(box.AmountMode) ||
                box.MinAmount <= 0 || box.MaxAmount < box.MinAmount ||
                (box.AmountMode == CurrencyBoxAmountMode.Fixed && box.MinAmount != box.MaxAmount) ||
                (box.AmountMode == CurrencyBoxAmountMode.UniformRandom && box.MinAmount == box.MaxAmount))
            {
                throw new InvalidDataException($"Currency box {box.ItemTemplateId} has an invalid reward definition.");
            }

            definitions.Add(new CurrencyBoxDefinition(
                box.ItemTemplateId,
                box.CurrencyType,
                box.AmountMode,
                box.MinAmount,
                box.MaxAmount));
        }

        return new CurrencyBoxCatalog(document.SchemaVersion, definitions.AsReadOnly());
    }

    private sealed class BoxCatalogDocument
    {
        public required int SchemaVersion { get; init; }
        public required List<BoxDocument?>? Boxes { get; init; }
    }

    private sealed class BoxDocument
    {
        public required int ItemTemplateId { get; init; }
        public required CurrencyType CurrencyType { get; init; }
        public required CurrencyBoxAmountMode AmountMode { get; init; }
        public required int MinAmount { get; init; }
        public required int MaxAmount { get; init; }
    }
}
