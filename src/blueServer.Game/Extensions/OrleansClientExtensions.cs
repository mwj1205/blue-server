using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;

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

        var clusteringMode = GetRequiredValue(
            section,
            "ClusteringMode");
        var useRedisClustering = GetUseRedisClustering(clusteringMode);
        var clusterId = GetRequiredValue(section, "ClusterId");
        var serviceId = GetRequiredValue(section, "ServiceId");
        var gatewayEndpoints = useRedisClustering
            ? null
            : ResolveGatewayEndpoints(
                GetRequiredValues(section, "GatewayHosts"),
                GetRequiredPort(section, "GatewayPort"));
        var redisConnectionString = useRedisClustering
            ? GetRequiredConnectionString(
                builder.Configuration,
                "OrleansRedis")
            : null;

        builder.UseOrleansClient(clientBuilder =>
        {
            clientBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            });

            if (useRedisClustering)
            {
                clientBuilder.UseRedisClustering(
                    redisConnectionString!);
            }
            else
            {
                clientBuilder.UseStaticClustering(
                    gatewayEndpoints!);
            }

            clientBuilder.AddActivityPropagation();
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

    private static string GetRequiredConnectionString(
        IConfiguration configuration,
        string name)
    {
        var value = configuration.GetConnectionString(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Connection string '{name}' is required when Orleans Redis clustering is enabled.");
        }

        return value;
    }

    private static bool GetUseRedisClustering(string value)
    {
        if (value.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new InvalidOperationException(
            $"Configuration value '{SectionName}:ClusteringMode' must be 'Development' or 'Redis'.");
    }

    private static string[] GetRequiredValues(
        IConfiguration configuration,
        string key)
    {
        var values = configuration
            .GetSection(key)
            .Get<string[]>()?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (values.Length == 0)
        {
            throw new InvalidOperationException(
                $"Configuration value '{SectionName}:{key}' must contain at least one host when Orleans is enabled.");
        }

        return values;
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

    private static IPEndPoint[] ResolveGatewayEndpoints(
        IEnumerable<string> hosts,
        int port)
    {
        var endpoints = new HashSet<IPEndPoint>();

        foreach (var host in hosts)
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host)
                    .Where(address =>
                        address.AddressFamily == AddressFamily.InterNetwork)
                    .Distinct()
                    .ToArray();

                if (addresses.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Gateway host '{host}' in configuration value '{SectionName}:GatewayHosts' did not resolve to an IPv4 address.");
                }

                foreach (var address in addresses)
                {
                    endpoints.Add(new IPEndPoint(address, port));
                }
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(
                    $"Gateway host '{host}' in configuration value '{SectionName}:GatewayHosts' could not be resolved.",
                    ex);
            }
        }

        return endpoints.ToArray();
    }
}
