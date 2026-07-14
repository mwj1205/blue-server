using blueServer.Admin.Clients;
using blueServer.Admin.Components;
using blueServer.Admin.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddOptions<GameApiOptions>()
    .BindConfiguration(GameApiOptions.SectionName)
    .Validate(options =>
        Uri.TryCreate(
            options.BaseAddress,
            UriKind.Absolute,
            out var baseAddress) &&
        (baseAddress.Scheme == Uri.UriSchemeHttp ||
            baseAddress.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrWhiteSpace(baseAddress.Host) &&
        string.IsNullOrEmpty(baseAddress.UserInfo) &&
        string.IsNullOrEmpty(baseAddress.Query) &&
        string.IsNullOrEmpty(baseAddress.Fragment),
        "GameApi:BaseAddress must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.")
    .Validate(options =>
        options.TimeoutSeconds is >= 1 and <= 30,
        "GameApi:TimeoutSeconds must be between 1 and 30.")
    .ValidateOnStart();

builder.Services.AddHttpClient<PlayerAdminClient>((services, client) =>
{
    var options = services
        .GetRequiredService<IOptions<GameApiOptions>>()
        .Value;

    client.BaseAddress = NormalizeBaseAddress(options.BaseAddress);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static Uri NormalizeBaseAddress(string value)
{
    var baseAddress = new Uri(value, UriKind.Absolute);

    if (baseAddress.AbsolutePath.EndsWith(
        "/",
        StringComparison.Ordinal))
    {
        return baseAddress;
    }

    return new UriBuilder(baseAddress)
    {
        Path = $"{baseAddress.AbsolutePath}/"
    }.Uri;
}
