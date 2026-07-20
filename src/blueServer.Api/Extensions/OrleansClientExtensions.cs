using System.Net;
using System.Net.Sockets;
using Orleans;
using Orleans.Configuration;

namespace blueServer.Api.Extensions;

public static class OrleansClientExtensions
{
    private const string SectionName = "Orleans";

    public static WebApplicationBuilder AddConfiguredOrleansClient(
        this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(SectionName);

        if (!section.GetValue<bool>("Enabled"))
        {
            return builder;
        }

        var clusterId = GetRequiredValue(section, "ClusterId");
        var serviceId = GetRequiredValue(section, "ServiceId");
        var gatewayHosts = GetRequiredValues(section, "GatewayHosts");
        var gatewayPort = GetRequiredPort(section, "GatewayPort");
        var gatewayEndpoints = ResolveGatewayEndpoints(
            gatewayHosts,
            gatewayPort);

        builder.Host.UseOrleansClient(clientBuilder =>
        {
            clientBuilder
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = clusterId;
                    options.ServiceId = serviceId;
                })
                .UseStaticClustering(gatewayEndpoints);
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
