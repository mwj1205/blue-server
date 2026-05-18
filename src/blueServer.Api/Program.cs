using blueServer.Api.Services;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using blueServer.Api.DTOs;
using blueServer.Api.Middlewares;
using FluentValidation;
using FluentValidation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddFluentValidationAutoValidation();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// AddDbContext: 필요할 때 GameDbContext 생성해달라고 등록하는 것.
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ASP.NET Core에게 PlayerService가 필요할 때마다 생성해달라고 등록하는 것.
builder.Services.AddScoped<PlayerService>();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () =>
{
    return "blueServer API is running!";
});

// POST: 이제 Entity 대신 CreatePlayerRequest DTO를 받음
app.MapPost("/players", async (
    PlayerService playerService,
    IValidator<CreatePlayerRequest> validator,
    CreatePlayerRequest request) =>
{
    var validationResult = await validator.ValidateAsync(request);

    if (!validationResult.IsValid)
    {
        return Results.BadRequest(
            validationResult.Errors.Select(x => new
            {
                field = x.PropertyName,
                message = x.ErrorMessage
            }));
    }

    var createdPlayer = await playerService.CreatePlayerAsync(request);

    return Results.Ok(createdPlayer);
});

// GET: 서비스에서 이미 Response DTO를 반환하므로 그대로 사용합니다.
app.MapGet("/players/{id:long}", async (PlayerService playerService, long id) =>
{
    var player = await playerService.GetPlayerByIdAsync(id);
    return player is null ? Results.NotFound() : Results.Ok(player);
});

app.Run();
