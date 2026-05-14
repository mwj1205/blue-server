using blueServer.Api.Services;
using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using blueServer.Api.DTOs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// AddDbContext: 필요할 때 GameDbContext 생성해달라고 등록하는 것.
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ASP.NET Core에게 PlayerService가 필요할 때마다 생성해달라고 등록하는 것.
builder.Services.AddScoped<PlayerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () =>
{
    return "blueServer API is running!";
});

// JSON -> C# 객체로 변환해서 받음
app.MapPost("/players", async (PlayerService playerService, CreatePlayerRequest request) =>
{
    var createdPlayer = await playerService.CreatePlayerAsync(request);

    return Results.Ok(createdPlayer);
});

app.MapGet("/players/{id:long}", async (PlayerService playerService, long id) =>
{
    var player = await playerService.GetPlayerByIdAsync(id);
    if (player is null)
  {
    return Results.NotFound();
  }

  return Results.Ok(player);
});

app.Run();
