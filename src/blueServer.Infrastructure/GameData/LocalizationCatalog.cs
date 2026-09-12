namespace blueServer.Infrastructure.GameData;

public sealed record LocalizationCatalog(
    int SchemaVersion,
    string Locale,
    IReadOnlyDictionary<string, string> Entries)
{
    public bool ContainsKey(string key)
    {
        return Entries.ContainsKey(key);
    }

    public string GetRequiredText(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Entries.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException(
                $"Localization key '{key}' was not found for locale '{Locale}'.");
    }
}
