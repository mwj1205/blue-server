using System.Text.Json;
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
var siloPort = GetRequiredPort(
    builder.Configuration,
    "Orleans:SiloPort");
var gatewayPort = GetRequiredPort(
    builder.Configuration,
    "Orleans:GatewayPort");

if (siloPort == gatewayPort)
{
    throw new InvalidOperationException(
        "Orleans silo and gateway ports must be different.");
}

builder.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering(
        siloPort: siloPort,
        gatewayPort: gatewayPort,
        serviceId: serviceId,
        clusterId: clusterId);
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
