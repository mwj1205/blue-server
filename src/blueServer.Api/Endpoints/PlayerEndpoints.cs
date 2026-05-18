using blueServer.Api.DTOs;
using blueServer.Api.Services;
using FluentValidation;

namespace blueServer.Api.Endpoints;

public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder
    MapPlayerEndpoints(
        this IEndpointRouteBuilder app)
    {
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

        app.MapGet("/players/{id:long}", async (
          PlayerService playerService,
          long id) =>
      {
          var player = await playerService.GetPlayerAsync(id);
          return player is null ? Results.NotFound() : Results.Ok(player);
      });

        return app;
    }
}
