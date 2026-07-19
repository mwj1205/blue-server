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

        var clusterId = GetRequiredValue(section, "ClusterId");
        var serviceId = GetRequiredValue(section, "ServiceId");
        var gatewayHost = GetRequiredValue(section, "GatewayHost");
        var gatewayPort = GetRequiredPort(section, "GatewayPort");
        var gatewayEndpoints = ResolveGatewayEndpoints(
            gatewayHost,
            gatewayPort);

        builder.UseOrleansClient(clientBuilder =>
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
        string host,
        int port)
    {
        try
        {
            var endpoints = Dns.GetHostAddresses(host)
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .Select(address => new IPEndPoint(address, port))
                .ToArray();

            if (endpoints.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Configuration value '{SectionName}:GatewayHost' did not resolve to an IPv4 address.");
            }

            return endpoints;
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Configuration value '{SectionName}:GatewayHost' could not be resolved.",
                ex);
        }
    }
}
