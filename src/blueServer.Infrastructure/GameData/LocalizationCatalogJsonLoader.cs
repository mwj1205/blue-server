using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace blueServer.Infrastructure.GameData;

public static class LocalizationCatalogJsonLoader
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<LocalizationCatalog> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        LocalizationDocument? document;

        try
        {
            document = await JsonSerializer.DeserializeAsync<LocalizationDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Localization data is not valid JSON.",
                exception);
        }

        if (document is null)
        {
            throw new InvalidDataException("Localization data is empty.");
        }

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Localization schema version {document.SchemaVersion} is not supported. " +
                $"Expected {SupportedSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(document.Locale))
        {
            throw new InvalidDataException("Localization locale is required.");
        }

        if (document.Entries is not { Count: > 0 })
        {
            throw new InvalidDataException(
                "Localization data must contain at least one entry.");
        }

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in document.Entries)
        {
            var key = entry.Key?.Trim();
            var value = entry.Value?.Trim();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    "Localization keys and values must not be empty.");
            }

            if (!entries.TryAdd(key, value))
            {
                throw new InvalidDataException(
                    $"Localization data contains duplicate key '{key}'.");
            }
        }

        return new LocalizationCatalog(
            document.SchemaVersion,
            document.Locale.Trim(),
            new ReadOnlyDictionary<string, string>(entries));
    }

    private sealed class LocalizationDocument
    {
        public int SchemaVersion { get; init; }
        public string? Locale { get; init; }
        public List<LocalizationEntryDocument>? Entries { get; init; }
    }

    private sealed class LocalizationEntryDocument
    {
        public string? Key { get; init; }
        public string? Value { get; init; }
    }
}
