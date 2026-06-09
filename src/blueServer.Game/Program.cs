using blueServer.Game;
using blueServer.Game.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using blueServer.Game.Repositories;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContextFactory<GameDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseNpgsql(connectionString);
});
builder.Services.AddSingleton<PlayerRepository>();

builder.Services.AddSingleton<LoginHandler>();
builder.Services.AddSingleton<ChatHandler>();
builder.Services.AddSingleton<PingHandler>();

builder.Services.AddSingleton<PacketDispatcher>();
builder.Services.AddSingleton<SessionFactory>();

builder.Services.AddHostedService<TcpListenerService>();

var host = builder.Build();
await host.RunAsync();