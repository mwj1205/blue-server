using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using blueServer.Admin.Configuration;
using blueServer.Admin.Models;
using Microsoft.Extensions.Options;

namespace blueServer.Admin.Clients;

public sealed class PlayerAdminClient
{
    private readonly HttpClient _httpClient;
    private readonly GameApiOptions _options;
    private readonly IHostEnvironment _hostEnvironment;

    public PlayerAdminClient(
        HttpClient httpClient,
        IOptions<GameApiOptions> options,
        IHostEnvironment hostEnvironment)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<PlayerDetails?> GetPlayerDetailsAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        EnsureDevelopmentLookupEnabled();

        using var timeoutCts = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var operationToken = timeoutCts.Token;
        var playerIdSegment = playerId.ToString(CultureInfo.InvariantCulture);

        using var profileResponse = await _httpClient.GetAsync(
            $"players/{playerIdSegment}",
            HttpCompletionOption.ResponseHeadersRead,
            operationToken);

        if (profileResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        profileResponse.EnsureSuccessStatusCode();

        var profile = await profileResponse.Content
            .ReadFromJsonAsync<PlayerSummary>(operationToken);

        if (profile is null)
        {
            throw new InvalidDataException(
                "Player profile response body was empty.");
        }

        using var rosterResponse = await _httpClient.GetAsync(
            $"players/{playerIdSegment}/characters",
            HttpCompletionOption.ResponseHeadersRead,
            operationToken);

        if (rosterResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        rosterResponse.EnsureSuccessStatusCode();

        var roster = await rosterResponse.Content
            .ReadFromJsonAsync<List<OwnedCharacterSummary>>(
                operationToken);

        if (roster is null)
        {
            throw new InvalidDataException(
                "Player roster response body was empty.");
        }

        return new PlayerDetails(profile, roster);
    }

    private void EnsureDevelopmentLookupEnabled()
    {
        if (!_hostEnvironment.IsDevelopment() ||
            !_options.EnableInsecurePlayerLookup)
        {
            throw new InvalidOperationException(
                "Unauthenticated player lookup is only available in Development.");
        }
    }
}
