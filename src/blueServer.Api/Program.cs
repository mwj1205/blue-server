using blueServer.Api.Services;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using blueServer.Api.DTOs;
using blueServer.Api.Middlewares;
using FluentValidation;
using FluentValidation.AspNetCore;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using blueServer.Domain.Entities;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddFluentValidationAutoValidation();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// AddDbContext: 필요할 때 GameDbContext 생성해달라고 등록하는 것.
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ASP.NET Core에게 PlayerService가 필요할 때마다 생성해달라고 등록하는 것.
builder.Services.AddScoped<PlayerService>();

builder.Services.AddScoped<JwtService>();

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = builder.Configuration["Jwt:Issuer"],

                ValidAudience = builder.Configuration["Jwt:Issuer"],

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            builder.Configuration["Jwt:Key"]!))
            };
    });

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();

app.UseAuthorization();

app.MapGet("/", () =>
{
    return "blueServer API is running!";
});

// // POST: 이제 Entity 대신 CreatePlayerRequest DTO를 받음
// app.MapPost("/players", async (
//     PlayerService playerService,
//     IValidator<CreatePlayerRequest> validator,
//     CreatePlayerRequest request) =>
// {
//     var validationResult = await validator.ValidateAsync(request);

//     if (!validationResult.IsValid)
//     {
//         return Results.BadRequest(
//             validationResult.Errors.Select(x => new
//             {
//                 field = x.PropertyName,
//                 message = x.ErrorMessage
//             }));
//     }

//     var createdPlayer = await playerService.CreatePlayerAsync(request);

//     return Results.Ok(createdPlayer);
// });

app.MapPost("/register", async (
    GameDbContext db,
    RegisterRequest request) =>
{
    var exists = await db.Players.AnyAsync(x => x.Nickname == request.Nickname);

    if (exists)
    {
        return Results.BadRequest(
            new
            {
                message = "Nickname already exists"
            });
    }

    var player = new Player
    {
        Nickname = request.Nickname,
        Password = request.Password,
        Gold = 1000,
        Gem = 500
    };

    db.Players.Add(player);

    await db.SaveChangesAsync();

    return Results.Ok();
});

app.MapPost("/login", async (
    GameDbContext db,
    JwtService jwtService,
    LoginRequest request) =>
{
    var player = await db.Players
        .FirstOrDefaultAsync(x =>
            x.Nickname == request.Nickname &&
            x.Password == request.Password);

    if (player is null)
    {
        return Results.BadRequest(
            new
            {
                message = "Invalid nickname or password"
            });
    }

    var token = jwtService.GenerateToken(player);

    return Results.Ok(new {token});
});

app.MapGet("/me", (
    ClaimsPrincipal user) =>
{
    var userId =
        user.FindFirstValue(
            ClaimTypes.NameIdentifier);

    var nickname =
        user.FindFirstValue(
            ClaimTypes.Name);

    return Results.Ok(new
    {
        userId,
        nickname
    });
})
.RequireAuthorization();

// GET: 서비스에서 이미 Response DTO를 반환하므로 그대로 사용합니다.
app.MapGet("/players/{id:long}", async (PlayerService playerService, long id) =>
{
    var player = await playerService.GetPlayerByIdAsync(id);
    return player is null ? Results.NotFound() : Results.Ok(player);
});

app.Run();
