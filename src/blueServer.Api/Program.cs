using blueServer.Api.Endpoints;
using blueServer.Api.Extensions;
using blueServer.Api.Middlewares;
using blueServer.Api.Services;
using blueServer.Infrastructure.Mails;
using blueServer.Infrastructure.Security;
using Elastic.Apm.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var orleansEnabled = builder.Configuration.GetValue<bool>(
    "Orleans:Enabled");
builder.AddConfiguredOrleansClient();

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

builder.Services.AddOpenApi();

if (builder.Configuration.GetValue<bool>(
        "Observability:ElasticApmEnabled"))
{
    builder.Services.AddElasticApmForAspNetCore(
        new EfCoreDiagnosticsSubscriber());
}

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddRedis(builder.Configuration);
builder.Services.AddJwt(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddValidation();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<MailListQueryService>();
builder.Services.AddScoped<MailDetailQueryService>();

if (orleansEnabled)
{
    builder.Services.AddScoped<
        IPlayerProfileQueryService,
        OrleansPlayerProfileQueryService>();
}
else
{
    builder.Services.AddScoped<
        IPlayerProfileQueryService,
        DatabasePlayerProfileQueryService>();
}

var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapPlayerEndpoints();
app.MapAuthEndpoints();
app.MapMailEndpoints();

app.Run();
