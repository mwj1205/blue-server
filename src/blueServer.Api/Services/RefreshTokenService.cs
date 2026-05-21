using StackExchange.Redis;

namespace blueServer.Api.Services;

public class RefreshTokenService
{
    private readonly IDatabase _redis;

    public RefreshTokenService(
        IConnectionMultiplexer redis)
    {
        _redis = redis.GetDatabase();
    }

    // refresh token 저장
    public async Task SaveRefreshTokenAsync(
        long playerId,
        string refreshToken)
    {
        await _redis.StringSetAsync(
            $"refresh_token:{playerId}", // key
            refreshToken,                // value
            TimeSpan.FromDays(7));       // TTL 7일
    }

    // refresh token 조회
    public async Task<string?> GetRefreshTokenAsync(
        long playerId)
    {
        // key로 refresh token 조회
        return await _redis.StringGetAsync(
            $"refresh_token:{playerId}");
    }

    // refresh token 삭제
    public async Task RemoveRefreshTokenAsync(
        long playerId)
    {
        await _redis.KeyDeleteAsync(
            $"refresh_token:{playerId}");
    }
}
