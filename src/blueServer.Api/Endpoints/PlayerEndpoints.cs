using System.Security.Claims;
using blueServer.Api.DTOs;
using blueServer.Api.Extensions;
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

        MapDevelopmentOnlyPlayerLookupEndpoints(app);

        app.MapGet("/players/me/profile", async (
            ClaimsPrincipal user,
            IPlayerProfileQueryService playerProfileQueryService,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetPlayerId(out var playerId))
            {
                return Results.Unauthorized();
            }

            var profile = await playerProfileQueryService.GetAsync(
                playerId,
                cancellationToken);

            return profile is null
                ? Results.NotFound()
                : Results.Ok(profile);
        })
        .RequireAuthorization();

        app.MapGet("/players/me/parties/{partyNo:int}", async (
            ClaimsPrincipal user,
            PartyService partyService,
            int partyNo,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetPlayerId(out var playerId))
            {
                return Results.Unauthorized();
            }

            if (!IsValidPartyNo(partyNo))
            {
                return Results.BadRequest(new
                {
                    message = $"Party no must be between {Party.MinPartyNo} and {Party.MaxPartyNo}"
                });
            }

            var party = await partyService.GetPartyAsync(
                playerId,
                partyNo,
                cancellationToken);

            return party is null
                ? Results.NotFound()
                : Results.Ok(party);
        }).RequireAuthorization();

        app.MapPut("/players/me/parties/{partyNo:int}", async (
            ClaimsPrincipal user,
            PartyService partyService,
            IValidator<SavePartyRequest> validator,
            int partyNo,
            SavePartyRequest request,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetPlayerId(out var playerId))
            {
                return Results.Unauthorized();
            }

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
                playerId,
                partyNo,
                request,
                cancellationToken);

            return party is null
                ? Results.NotFound()
                : Results.Ok(party);
        }).RequireAuthorization();

        return app;
    }

    private static void MapDevelopmentOnlyPlayerLookupEndpoints(
        IEndpointRouteBuilder app)
    {
        var environment = app.ServiceProvider
            .GetRequiredService<IHostEnvironment>();

        if (!environment.IsDevelopment())
        {
            return;
        }

        app.MapGet("/players/{id:long}", async (
            PlayerService playerService,
            long id,
            CancellationToken cancellationToken) =>
        {
            var player = await playerService.GetPlayerAsync(
                id,
                cancellationToken);

            return player is null
                ? Results.NotFound()
                : Results.Ok(player);
        });

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
    }

    private static bool IsValidPartyNo(int partyNo)
    {
        return partyNo is >= Party.MinPartyNo and <= Party.MaxPartyNo;
    }

}
