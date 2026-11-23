using System.Security.Claims;
using blueServer.Api.DTOs;
using blueServer.Api.Extensions;
using blueServer.Domain.Entities;
using blueServer.Infrastructure.Items;

namespace blueServer.Api.Endpoints;

public static class CurrencyBoxEndpoints
{
    public static IEndpointRouteBuilder MapCurrencyBoxEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/players/me/currency-boxes")
            .RequireAuthorization();

        group.MapPost("/{itemTemplateId:int}/use", UseCurrencyBoxAsync);
        return app;
    }

    private static async Task<IResult> UseCurrencyBoxAsync(
        ClaimsPrincipal user,
        CurrencyBoxUseService currencyBoxUseService,
        int itemTemplateId,
        CurrencyBoxUseBody request,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        if (itemTemplateId <= 0)
        {
            return Results.BadRequest(new { message = "Item template id must be greater than zero." });
        }

        if (request.Quantity is < 1 or > ItemTemplate.InventoryMaxQuantity)
        {
            return Results.BadRequest(new
            {
                message = $"Quantity must be between 1 and {ItemTemplate.InventoryMaxQuantity}."
            });
        }

        if (request.RequestId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Request id must not be empty." });
        }

        var result = await currencyBoxUseService.UseAsync(
            playerId,
            new CurrencyBoxUseRequest(itemTemplateId, request.Quantity, request.RequestId),
            DateTime.UtcNow,
            cancellationToken);

        return ToResponse(result);
    }

    private static IResult ToResponse(CurrencyBoxUseResult result)
    {
        if (result.Status == CurrencyBoxUseStatus.Used && result.Snapshot is { } snapshot)
        {
            return Results.Ok(new CurrencyBoxUseResponse(
                snapshot.RequestId,
                snapshot.ItemTemplateId,
                snapshot.UsedQuantity,
                snapshot.QuantityAfter,
                snapshot.CurrencyType.ToString(),
                snapshot.GrantedAmount,
                snapshot.CurrentGold,
                snapshot.CurrentGem,
                snapshot.ProcessedAt));
        }

        return result.Status switch
        {
            CurrencyBoxUseStatus.PlayerNotFound or CurrencyBoxUseStatus.BoxNotFound =>
                Results.NotFound(new { code = result.Status.ToString() }),
            CurrencyBoxUseStatus.InsufficientQuantity => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Not enough currency boxes."
            }),
            CurrencyBoxUseStatus.ItemTemplateNotFound or CurrencyBoxUseStatus.ItemTemplateInactive =>
                Results.Conflict(new
                {
                    code = result.Status.ToString(),
                    message = "The currency box item is unavailable."
                }),
            CurrencyBoxUseStatus.IdempotencyConflict => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Request id was already used with a different payload."
            }),
            CurrencyBoxUseStatus.ConcurrencyConflict => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Inventory or currency state changed. Retry with the same request id."
            }),
            _ => throw new InvalidOperationException(
                $"Unexpected currency box use result. Status={result.Status}")
        };
    }
}
