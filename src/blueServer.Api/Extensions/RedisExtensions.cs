using StackExchange.Redis;

namespace blueServer.Api.Extensions;

public static class RedisExtensions
{
    public static IServiceCollection AddRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Redis connection string is missing in appsettings.");
        }

        var redis = ConnectionMultiplexer.Connect(connectionString);

        services.AddSingleton<IConnectionMultiplexer>(redis);
        return services;
    }
}
