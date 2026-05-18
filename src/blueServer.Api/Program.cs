using blueServer.Api.Endpoints;
using blueServer.Api.Extensions;
using blueServer.Api.Middlewares;
using blueServer.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDatabase(builder.Configuration);

builder.Services.AddJwt(builder.Configuration);

builder.Services.AddAuthorization();

builder.Services.AddValidation();

builder.Services.AddScoped<PlayerService>();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

app.MapPlayerEndpoints();

app.MapAuthEndpoints();

app.Run();