using System.Security.Claims;
using blueServer.Api.DTOs;
using blueServer.Api.Extensions;
using blueServer.Infrastructure.Mails;
using Microsoft.AspNetCore.Http.Features;

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
        MailClaimBody request,
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

        if (request.RequestId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Request id must not be empty." });
        }

        var result = await mailClaimService.ClaimAsync(
            playerId,
            new MailClaimRequest(mailId, request.RequestId),
            DateTime.UtcNow,
            cancellationToken);

        return ToResponse(result, request.RequestId);
    }

    private static async Task<IResult> ClaimAllMailAsync(
        ClaimsPrincipal user,
        MailClaimAllService mailClaimAllService,
        HttpRequest httpRequest,
        MailClaimAllBody? request,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        if (request is not null)
        {
            if (request.RequestId == Guid.Empty)
            {
                return Results.BadRequest(new { message = "Request id must not be empty." });
            }

            var requestResult = await mailClaimAllService.ClaimAllAsync(
                playerId,
                new MailClaimAllRequest(request.RequestId),
                DateTime.UtcNow,
                cancellationToken);

            return ToResponse(requestResult, request.RequestId);
        }

        // 본문을 보낸 요청이 JSON null 등으로 바인딩되어도 기존 지급 경로로 보내지 않는다.
        if (httpRequest.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return Results.BadRequest(new { message = "Request body must contain a request id." });
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
            attachments)
        {
            ItemAttachments = mail.ItemAttachments.Select(attachment => new MailItemAttachmentResponse(
                attachment.ItemTemplateId,
                attachment.Quantity,
                attachment.ClaimedQuantity,
                attachment.RemainingQuantity)).ToArray()
        };
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

    private static IResult ToResponse(MailClaimRequestResult result, Guid requestId)
    {
        return result.Status switch
        {
            MailClaimRequestStatus.Claimed or
                MailClaimRequestStatus.PartiallyClaimed or
                MailClaimRequestStatus.NoCapacity or
                MailClaimRequestStatus.AlreadyClaimed =>
                Results.Ok(new MailClaimResponse(
                    result.MailClaimedAt,
                    result.CurrentGold,
                    result.CurrentGem,
                    result.Status == MailClaimRequestStatus.AlreadyClaimed,
                    requestId,
                    result.Status.ToString(),
                    result.ProcessedAt,
                    result.GrantedGold,
                    result.GrantedGem,
                    result.Items.Select(item => new MailClaimItemResponse(
                        item.ItemTemplateId, item.GrantedQuantity,
                        item.RemainingQuantity, item.QuantityAfter)).ToArray())),
            MailClaimRequestStatus.NotFound or
                MailClaimRequestStatus.PlayerNotFound => Results.NotFound(),
            MailClaimRequestStatus.Expired => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Mail has expired."
            }),
            MailClaimRequestStatus.NoRewards => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Mail has no rewards to claim."
            }),
            MailClaimRequestStatus.ConcurrencyConflict => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Mail state changed. Reload the Mail and try again."
            }),
            MailClaimRequestStatus.IdempotencyConflict => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Mail reward state conflicts with the completed request."
            }),
            MailClaimRequestStatus.ItemTemplateNotFound or
                MailClaimRequestStatus.ItemTemplateInactive => Results.Conflict(new
                {
                    code = result.Status.ToString(),
                    message = "An attached item is unavailable. No rewards were applied."
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

    private static IResult ToResponse(MailClaimAllRequestResult result, Guid requestId)
    {
        return result.Status switch
        {
            MailClaimAllRequestStatus.Processed or MailClaimAllRequestStatus.NothingToClaim =>
                Results.Ok(new MailClaimAllRequestResponse(
                    requestId,
                    result.Status.ToString(),
                    result.ProcessedAt,
                    result.ClaimedMailCount,
                    result.GrantedGold,
                    result.GrantedGem,
                    result.CurrentGold,
                    result.CurrentGem,
                    result.HasMore,
                    result.Entries.Select(ToResponse).ToArray())),
            MailClaimAllRequestStatus.PlayerNotFound => Results.NotFound(),
            MailClaimAllRequestStatus.ConcurrencyConflict => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Mail state changed. Reload the Mail list and try again."
            }),
            MailClaimAllRequestStatus.IdempotencyConflict => Results.Conflict(new
            {
                code = result.Status.ToString(),
                message = "Mail reward state conflicts with a completed request."
            }),
            MailClaimAllRequestStatus.ItemTemplateNotFound or
                MailClaimAllRequestStatus.ItemTemplateInactive => Results.Conflict(new
                {
                    code = result.Status.ToString(),
                    message = "An attached item is unavailable. No rewards were applied."
                }),
            _ => throw new InvalidOperationException(
                $"Unexpected Mail claim-all request result. Status={result.Status}")
        };
    }

    private static MailClaimAllEntryResponse ToResponse(MailClaimAllEntryResult entry)
    {
        return new MailClaimAllEntryResponse(
            entry.MailId,
            entry.Claim.Status.ToString(),
            entry.Claim.MailClaimedAt,
            entry.Claim.GrantedGold,
            entry.Claim.GrantedGem,
            entry.Claim.Items.Select(item => new MailClaimItemResponse(
                item.ItemTemplateId,
                item.GrantedQuantity,
                item.RemainingQuantity,
                item.QuantityAfter)).ToArray());
    }

    private static IResult InvalidMailId()
    {
        return Results.BadRequest(new
        {
            message = "Mail id must be greater than zero."
        });
    }
}
