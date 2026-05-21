using StackExchange.Redis;

namespace blueServer.Api.Extensions;

public static class RedisExtensions
{
    public static IServiceCollection AddRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redis = ConnectionMultiplexer.Connect("localhost:6379");

        services.AddSingleton<IConnectionMultiplexer>(redis);

        return services;
    }
}
