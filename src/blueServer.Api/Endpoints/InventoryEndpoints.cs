using System.Security.Claims;
using blueServer.Api.DTOs;
using blueServer.Api.Extensions;
using blueServer.Infrastructure.Items;

namespace blueServer.Api.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/players/me/inventory")
            .RequireAuthorization();

        group.MapGet("/", GetInventoryAsync);
        return app;
    }

    private static async Task<IResult> GetInventoryAsync(
        ClaimsPrincipal user,
        InventoryListQueryService inventoryListQueryService,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var result = await inventoryListQueryService.GetAsync(
            playerId,
            cancellationToken);

        return result.Status switch
        {
            InventoryListStatus.Success => Results.Ok(new InventoryListResponse(
                result.Items.Select(item => new InventoryItemResponse(
                    item.ItemTemplateId,
                    item.Quantity,
                    item.Type.ToString(),
                    item.IsActive)).ToArray())),
            InventoryListStatus.PlayerNotFound => Results.NotFound(new
            {
                code = result.Status.ToString()
            }),
            // 요청 크기 문제가 아니라 서버의 전체 목록 표현 한계를 넘은 상태다.
            InventoryListStatus.InventoryTooLarge => Results.Json(new
            {
                code = result.Status.ToString(),
                message = "Inventory exceeds the supported response size."
            }, statusCode: StatusCodes.Status500InternalServerError),
            _ => throw new InvalidOperationException(
                $"Unexpected inventory query result. Status={result.Status}")
        };
    }
}
