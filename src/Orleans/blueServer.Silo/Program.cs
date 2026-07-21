using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using blueServer.Infrastructure;
using Elastic.Apm.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
    options.JsonWriterOptions = new JsonWriterOptions
    {
        Indented = false
    };
});

var clusteringMode = GetRequiredValue(
    builder.Configuration,
    "Orleans:ClusteringMode");
var useRedisClustering = GetUseRedisClustering(clusteringMode);
var hostingMode = GetRequiredValue(
    builder.Configuration,
    "Orleans:HostingMode");
var useKubernetesHosting = GetUseKubernetesHosting(hostingMode);

if (useKubernetesHosting && !useRedisClustering)
{
    throw new InvalidOperationException(
        "Orleans Kubernetes hosting currently requires Redis clustering.");
}

var clusterId = useKubernetesHosting
    ? null
    : GetRequiredValue(
        builder.Configuration,
        "Orleans:ClusterId");
var serviceId = useKubernetesHosting
    ? null
    : GetRequiredValue(
        builder.Configuration,
        "Orleans:ServiceId");
var siloName = useKubernetesHosting
    ? null
    : GetRequiredValue(
        builder.Configuration,
        "Orleans:SiloName");
var advertisedHost = useKubernetesHosting
    ? null
    : GetRequiredValue(
        builder.Configuration,
        "Orleans:AdvertisedHost");
var siloPort = GetRequiredPort(
    builder.Configuration,
    "Orleans:SiloPort");
var gatewayPort = GetRequiredPort(
    builder.Configuration,
    "Orleans:GatewayPort");
var advertisedAddress = useKubernetesHosting
    ? null
    : ResolveAddress(
        advertisedHost!,
        "Orleans:AdvertisedHost");
var primarySiloAddress = useRedisClustering
    ? null
    : ResolveAddress(
        GetRequiredValue(
            builder.Configuration,
            "Orleans:PrimarySiloHost"),
        "Orleans:PrimarySiloHost");
var redisConnectionString = useRedisClustering
    ? GetRequiredConnectionString(
        builder.Configuration,
        "OrleansRedis")
    : null;
var connectionString = GetRequiredValue(
    builder.Configuration,
    "ConnectionStrings:Default");

if (siloPort == gatewayPort)
{
    throw new InvalidOperationException(
        "Orleans silo and gateway ports must be different.");
}

builder.Services.AddPooledDbContextFactory<GameDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

if (builder.Configuration.GetValue<bool>(
        "Observability:ElasticApmEnabled"))
{
    builder.Services.AddElasticApm(
        new EfCoreDiagnosticsSubscriber());
}

builder.UseOrleans(siloBuilder =>
{
    if (useKubernetesHosting)
    {
        siloBuilder
            .Configure<EndpointOptions>(options =>
            {
                options.SiloPort = siloPort;
                options.GatewayPort = gatewayPort;
            })
            .UseKubernetesHosting();
    }
    else
    {
        siloBuilder
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId!;
                options.ServiceId = serviceId!;
            })
            .Configure<SiloOptions>(options =>
            {
                options.SiloName = siloName!;
            })
            .ConfigureEndpoints(
                advertisedIP: advertisedAddress!,
                siloPort: siloPort,
                gatewayPort: gatewayPort,
                listenOnAnyHostAddress: true);
    }

    if (useRedisClustering)
    {
        siloBuilder.UseRedisClustering(redisConnectionString!);
    }
    else
    {
        siloBuilder.UseDevelopmentClustering(
            new IPEndPoint(primarySiloAddress!, siloPort));
    }

    siloBuilder.AddActivityPropagation();
});

await builder.Build().RunAsync();

static string GetRequiredValue(
    IConfiguration configuration,
    string key)
{
    var value = configuration[key];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' is required.");
    }

    return value;
}

static string GetRequiredConnectionString(
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

static bool GetUseRedisClustering(string value)
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
        "Configuration value 'Orleans:ClusteringMode' must be 'Development' or 'Redis'.");
}

static bool GetUseKubernetesHosting(string value)
{
    if (value.Equals("Kubernetes", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (value.Equals("Manual", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    throw new InvalidOperationException(
        "Configuration value 'Orleans:HostingMode' must be 'Manual' or 'Kubernetes'.");
}

static int GetRequiredPort(
    IConfiguration configuration,
    string key)
{
    var port = configuration.GetValue<int?>(key);

    if (port is null or < 1 or > 65_535)
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' must be a port between 1 and 65535.");
    }

    return port.Value;
}

static IPAddress ResolveAddress(
    string host,
    string configurationKey)
{
    try
    {
        var address = Dns.GetHostAddresses(host)
            .FirstOrDefault(candidate =>
                candidate.AddressFamily == AddressFamily.InterNetwork);

        return address ?? throw new InvalidOperationException(
            $"Configuration value '{configurationKey}' did not resolve to an IPv4 address.");
    }
    catch (SocketException ex)
    {
        throw new InvalidOperationException(
            $"Configuration value '{configurationKey}' could not be resolved.",
            ex);
    }
}
