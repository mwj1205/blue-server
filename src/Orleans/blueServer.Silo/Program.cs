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

var clusterId = GetRequiredValue(
    builder.Configuration,
    "Orleans:ClusterId");
var serviceId = GetRequiredValue(
    builder.Configuration,
    "Orleans:ServiceId");
var siloName = GetRequiredValue(
    builder.Configuration,
    "Orleans:SiloName");
var advertisedHost = GetRequiredValue(
    builder.Configuration,
    "Orleans:AdvertisedHost");
var primarySiloHost = GetRequiredValue(
    builder.Configuration,
    "Orleans:PrimarySiloHost");
var siloPort = GetRequiredPort(
    builder.Configuration,
    "Orleans:SiloPort");
var gatewayPort = GetRequiredPort(
    builder.Configuration,
    "Orleans:GatewayPort");
var advertisedAddress = ResolveAddress(
    advertisedHost,
    "Orleans:AdvertisedHost");
var primarySiloAddress = ResolveAddress(
    primarySiloHost,
    "Orleans:PrimarySiloHost");
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
    siloBuilder
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterId;
            options.ServiceId = serviceId;
        })
        .Configure<SiloOptions>(options =>
        {
            options.SiloName = siloName;
        })
        .UseDevelopmentClustering(
            new IPEndPoint(primarySiloAddress, siloPort))
        .ConfigureEndpoints(
            advertisedIP: advertisedAddress,
            siloPort: siloPort,
            gatewayPort: gatewayPort,
            listenOnAnyHostAddress: true)
        .AddActivityPropagation();
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
