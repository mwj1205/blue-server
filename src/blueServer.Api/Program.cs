using blueServer.Api.Endpoints;
using blueServer.Api.Extensions;
using blueServer.Api.Middlewares;
using blueServer.Api.Services;
using blueServer.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddRedis(builder.Configuration);
builder.Services.AddJwt(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddValidation();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<RefreshTokenService>();
var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapPlayerEndpoints();
app.MapAuthEndpoints();

app.Run();
