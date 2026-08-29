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
}
