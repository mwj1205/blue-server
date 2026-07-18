using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans;

namespace blueServer.Game.Extensions;

public static class OrleansClientExtensions
{
    private const string SectionName = "Orleans";

    public static HostApplicationBuilder AddConfiguredOrleansClient(
        this HostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(SectionName);

        if (!section.GetValue<bool>("Enabled"))
        {
            return builder;
        }

        var clusterId = GetRequiredValue(section, "ClusterId");
        var serviceId = GetRequiredValue(section, "ServiceId");
        var gatewayPort = GetRequiredPort(section, "GatewayPort");

        builder.UseOrleansClient(clientBuilder =>
        {
            clientBuilder.UseLocalhostClustering(
                gatewayPort: gatewayPort,
                serviceId: serviceId,
                clusterId: clusterId);
        });

        return builder;
    }

    private static string GetRequiredValue(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuration value '{SectionName}:{key}' is required when Orleans is enabled.");
        }

        return value;
    }

    private static int GetRequiredPort(
        IConfiguration configuration,
        string key)
    {
        var port = configuration.GetValue<int?>(key);

        if (port is null or < 1 or > 65_535)
        {
            throw new InvalidOperationException(
                $"Configuration value '{SectionName}:{key}' must be a port between 1 and 65535 when Orleans is enabled.");
        }

        return port.Value;
    }
}
