using System.Security.Claims;
using blueServer.Api.DTOs;
using blueServer.Api.Extensions;
using blueServer.Infrastructure.Mails;

namespace blueServer.Api.Endpoints;

public static class MailEndpoints
{
    public static IEndpointRouteBuilder MapMailEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/players/me/mails")
            .RequireAuthorization();

        group.MapGet("/", GetMailListAsync);
        group.MapGet("/{mailId:long}", GetMailDetailAsync);
        group.MapPut("/{mailId:long}/read", MarkMailAsReadAsync);
        group.MapPost("/{mailId:long}/claim", ClaimMailAsync);
        group.MapPost("/claim-all", ClaimAllMailAsync);

        return app;
    }

    private static async Task<IResult> GetMailListAsync(
        ClaimsPrincipal user,
        MailListQueryService mailListQueryService,
        int? pageSize,
        DateTimeOffset? cursorSentAt,
        long? cursorId,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var resolvedPageSize = pageSize ??
            MailListQueryService.DefaultPageSize;

        if (resolvedPageSize is < 1 or > MailListQueryService.MaxPageSize)
        {
            return Results.BadRequest(new
            {
                message = $"Page size must be between 1 and {MailListQueryService.MaxPageSize}."
            });
        }

        if (cursorSentAt.HasValue != cursorId.HasValue)
        {
            return Results.BadRequest(new
            {
                message = "Cursor sent time and id must be provided together."
            });
        }

        if (cursorId is <= 0)
        {
            return Results.BadRequest(new
            {
                message = "Cursor id must be greater than zero."
            });
        }

        var cursor = cursorSentAt.HasValue
            ? new MailListCursor(
                cursorSentAt.Value.UtcDateTime,
                cursorId!.Value)
            : null;

        var result = await mailListQueryService.GetAsync(
            playerId,
            DateTime.UtcNow,
            resolvedPageSize,
            cursor,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> GetMailDetailAsync(
        ClaimsPrincipal user,
        MailDetailQueryService mailDetailQueryService,
        long mailId,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        if (mailId <= 0)
        {
            return Results.BadRequest(new
            {
                message = "Mail id must be greater than zero."
            });
        }

        var result = await mailDetailQueryService.GetAsync(
            playerId,
            mailId,
            DateTime.UtcNow,
            cancellationToken);

        return result.Mail is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(result.Mail));
    }

    private static async Task<IResult> MarkMailAsReadAsync(
        ClaimsPrincipal user,
        MailReadService mailReadService,
        long mailId,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        if (mailId <= 0)
        {
            return InvalidMailId();
        }

        var result = await mailReadService.MarkAsReadAsync(
            playerId,
            mailId,
            DateTime.UtcNow,
            cancellationToken);

        return ToResponse(result);
    }

    private static async Task<IResult> ClaimMailAsync(
        ClaimsPrincipal user,
        MailClaimService mailClaimService,
        long mailId,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        if (mailId <= 0)
        {
            return InvalidMailId();
        }

        var result = await mailClaimService.ClaimAsync(
            playerId,
            mailId,
            DateTime.UtcNow,
            cancellationToken);

        return ToResponse(result);
    }

    private static async Task<IResult> ClaimAllMailAsync(
        ClaimsPrincipal user,
        MailClaimAllService mailClaimAllService,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var result = await mailClaimAllService.ClaimAllAsync(
            playerId,
            DateTime.UtcNow,
            cancellationToken);

        return ToResponse(result);
    }

    private static MailListResponse ToResponse(
        MailListResult result)
    {
        var items = result.Items
            .Select(item => new MailListItemResponse(
                item.Id,
                item.Title,
                item.SentAt,
                item.ExpiresAt,
                item.IsRead,
                item.IsClaimed,
                item.IsExpired,
                item.CanClaim,
                item.AttachmentCount))
            .ToArray();

        var nextCursor = result.NextCursor is null
            ? null
            : new MailListCursorResponse(
                result.NextCursor.SentAt,
                result.NextCursor.Id);

        return new MailListResponse(items, nextCursor);
    }

    private static MailDetailResponse ToResponse(
        MailDetail mail)
    {
        var attachments = mail.Attachments
            .Select(attachment => new MailAttachmentResponse(
                attachment.Type,
                attachment.Amount))
            .ToArray();

        return new MailDetailResponse(
            mail.Id,
            mail.Title,
            mail.Body,
            mail.SentAt,
            mail.ExpiresAt,
            mail.ReadAt,
            mail.ClaimedAt,
            mail.IsRead,
            mail.IsClaimed,
            mail.IsExpired,
            mail.CanClaim,
            attachments);
    }

    private static IResult ToResponse(MailReadResult result)
    {
        return result.Status switch
        {
            MailReadStatus.MarkedAsRead when result.ReadAt.HasValue =>
                Results.Ok(new MailReadResponse(
                    result.ReadAt.Value,
                    false)),
            MailReadStatus.AlreadyRead when result.ReadAt.HasValue =>
                Results.Ok(new MailReadResponse(
                    result.ReadAt.Value,
                    true)),
            MailReadStatus.NotFound => Results.NotFound(),
            MailReadStatus.ConcurrencyConflict => Results.Conflict(new
            {
                message = "Mail state changed. Reload the Mail and try again."
            }),
            _ => throw new InvalidOperationException(
                $"Unexpected Mail read result. Status={result.Status}")
        };
    }

    private static IResult ToResponse(MailClaimResult result)
    {
        return result.Status switch
        {
            MailClaimStatus.Claimed when result.ClaimedAt.HasValue =>
                Results.Ok(new MailClaimResponse(
                    result.ClaimedAt.Value,
                    result.CurrentGold,
                    result.CurrentGem,
                    false)),
            MailClaimStatus.AlreadyClaimed when result.ClaimedAt.HasValue =>
                Results.Ok(new MailClaimResponse(
                    result.ClaimedAt.Value,
                    result.CurrentGold,
                    result.CurrentGem,
                    true)),
            MailClaimStatus.NotFound or
                MailClaimStatus.PlayerNotFound => Results.NotFound(),
            MailClaimStatus.Expired => Results.Conflict(new
            {
                message = "Mail has expired."
            }),
            MailClaimStatus.NoRewards => Results.Conflict(new
            {
                message = "Mail has no rewards to claim."
            }),
            MailClaimStatus.ConcurrencyConflict => Results.Conflict(new
            {
                message = "Mail state changed. Reload the Mail and try again."
            }),
            MailClaimStatus.IdempotencyConflict => Results.Conflict(new
            {
                message = "Mail reward state conflicts with the completed request."
            }),
            _ => throw new InvalidOperationException(
                $"Unexpected Mail claim result. Status={result.Status}")
        };
    }

    private static IResult ToResponse(MailClaimAllResult result)
    {
        return result.Status switch
        {
            MailClaimAllStatus.Claimed or
                MailClaimAllStatus.NothingToClaim =>
                Results.Ok(new MailClaimAllResponse(
                    result.ClaimedMailCount,
                    result.GrantedGold,
                    result.GrantedGem,
                    result.CurrentGold,
                    result.CurrentGem,
                    result.HasMore)),
            MailClaimAllStatus.PlayerNotFound => Results.NotFound(),
            MailClaimAllStatus.ConcurrencyConflict => Results.Conflict(new
            {
                message = "Mail state changed. Reload the Mail list and try again."
            }),
            MailClaimAllStatus.IdempotencyConflict => Results.Conflict(new
            {
                message = "Mail reward state conflicts with a completed request."
            }),
            _ => throw new InvalidOperationException(
                $"Unexpected Mail claim-all result. Status={result.Status}")
        };
    }

    private static IResult InvalidMailId()
    {
        return Results.BadRequest(new
        {
            message = "Mail id must be greater than zero."
        });
    }
}
