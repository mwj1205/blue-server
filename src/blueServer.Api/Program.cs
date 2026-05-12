using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// AddDbContext: 필요할 때 GameDbContext 생성해달라고 등록하는 것.
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "blueServer API is running!");

// JSON -> C# 객체로 변환해서 받음
app.MapPost("/players", async (GameDbContext db, Player player) =>
{
    db.Players.Add(player);
    await db.SaveChangesAsync(); // 실제 DB에 저장
    return Results.Created($"/players/{player.Id}", player);
});

app.MapGet("/players/{id:long}", async (GameDbContext db, long id) =>
{
    var player = await db.Players.FindAsync(id);
    if (player is null)
  {
    return Results.NotFound();
  }

  return Results.Ok(player);
});

app.Run();
