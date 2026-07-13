using blueServer.Game;
using blueServer.Game.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using blueServer.Game.Repositories;
using blueServer.Game.Packets;
using blueServer.Game.Services;
using Microsoft.Extensions.Configuration;
using blueServer.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddDbContext<GameDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<PlayerRepository>();
builder.Services.AddScoped<OwnedCharacterRepository>();
builder.Services.AddScoped<CharacterTemplateRepository>();
builder.Services.AddScoped<CharacterGachaService>();
builder.Services.AddScoped<OwnedCharacterListService>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<StageClearService>();
builder.Services.AddScoped<PlayerProfileService>();
builder.Services.AddScoped<GameJwtValidator>();

builder.Services.AddKeyedScoped<IPacketHandler, LoginHandler>(Opcode.Login);
builder.Services.AddKeyedScoped<IPacketHandler, ChatHandler>(Opcode.Chat);
builder.Services.AddKeyedScoped<IPacketHandler, PingHandler>(Opcode.Ping);
builder.Services.AddKeyedScoped<IPacketHandler, CharacterGachaHandler>(Opcode.CharacterGacha);
builder.Services.AddKeyedScoped<IPacketHandler, OwnedCharacterListHandler>(Opcode.OwnedCharacterList);
builder.Services.AddKeyedScoped<IPacketHandler, PartyGetHandler>(Opcode.PartyGet);
builder.Services.AddKeyedScoped<IPacketHandler, PartySaveHandler>(Opcode.PartySave);
builder.Services.AddKeyedScoped<IPacketHandler, StageClearHandler>(Opcode.StageClear);
builder.Services.AddKeyedScoped<IPacketHandler, PlayerProfileHandler>(Opcode.PlayerProfile);

builder.Services.AddSingleton<PacketDispatcher>();
builder.Services.AddSingleton<SessionFactory>();
builder.Services.AddSingleton<SessionTaskTracker>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<SessionMonitor>();

builder.Services.AddHostedService<TcpListenerService>();

var host = builder.Build();
await host.RunAsync();
