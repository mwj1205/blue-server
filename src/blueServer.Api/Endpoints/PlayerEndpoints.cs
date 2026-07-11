using System.Security.Claims;
using blueServer.Api.DTOs;
using blueServer.Api.Services;
using blueServer.Domain.Entities;
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

        app.MapGet("/players/me/profile", async (
            ClaimsPrincipal user,
            PlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetPlayerId(user, out var playerId))
            {
                return Results.Unauthorized();
            }

            var profile = await playerService.GetProfileAsync(
                playerId,
                cancellationToken);

            return profile is null
                ? Results.NotFound()
                : Results.Ok(profile);
        })
        .RequireAuthorization();

        app.MapGet("/players/{id:long}/characters", async (
            PlayerService playerService,
            long id,
            CancellationToken cancellationToken) =>
        {
            var characters = await playerService.GetOwnedCharactersAsync(
                id,
                cancellationToken);

            return characters is null
                ? Results.NotFound()
                : Results.Ok(characters);
        });

        app.MapGet("/players/{id:long}/parties/{partyNo:int}", async (
            PartyService partyService,
            long id,
            int partyNo,
            CancellationToken cancellationToken) =>
        {
            if (!IsValidPartyNo(partyNo))
            {
                return Results.BadRequest(new
                {
                    message = $"Party no must be between {Party.MinPartyNo} and {Party.MaxPartyNo}"
                });
            }

            var party = await partyService.GetPartyAsync(
                id,
                partyNo,
                cancellationToken);

            return party is null
                ? Results.NotFound()
                : Results.Ok(party);
        });

        app.MapPut("/players/{id:long}/parties/{partyNo:int}", async (
            PartyService partyService,
            IValidator<SavePartyRequest> validator,
            long id,
            int partyNo,
            SavePartyRequest request,
            CancellationToken cancellationToken) =>
        {
            if (!IsValidPartyNo(partyNo))
            {
                return Results.BadRequest(new
                {
                    message = $"Party no must be between {Party.MinPartyNo} and {Party.MaxPartyNo}"
                });
            }

            var validationResult = await validator.ValidateAsync(
                request,
                cancellationToken);

            if (!validationResult.IsValid)
            {
                return Results.BadRequest(
                    validationResult.Errors.Select(x => new
                    {
                        field = x.PropertyName,
                        message = x.ErrorMessage
                    }));
            }

            var party = await partyService.SavePartyAsync(
                id,
                partyNo,
                request,
                cancellationToken);

            return party is null
                ? Results.NotFound()
                : Results.Ok(party);
        });

        return app;
    }

    private static bool IsValidPartyNo(int partyNo)
    {
        return partyNo is >= Party.MinPartyNo and <= Party.MaxPartyNo;
    }

    private static bool TryGetPlayerId(
        ClaimsPrincipal user,
        out long playerId)
    {
        var playerIdClaim = user.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return long.TryParse(
            playerIdClaim,
            out playerId);
    }
}
