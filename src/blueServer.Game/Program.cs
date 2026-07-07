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

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddScoped<GameJwtValidator>();

builder.Services.AddKeyedScoped<IPacketHandler, LoginHandler>(Opcode.Login);
builder.Services.AddKeyedScoped<IPacketHandler, ChatHandler>(Opcode.Chat);
builder.Services.AddKeyedScoped<IPacketHandler, PingHandler>(Opcode.Ping);
builder.Services.AddKeyedScoped<IPacketHandler, CharacterGachaHandler>(Opcode.CharacterGacha);

builder.Services.AddSingleton<PacketDispatcher>();
builder.Services.AddSingleton<SessionFactory>();
builder.Services.AddSingleton<SessionTaskTracker>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<SessionMonitor>();

builder.Services.AddHostedService<TcpListenerService>();

var host = builder.Build();
await host.RunAsync();
