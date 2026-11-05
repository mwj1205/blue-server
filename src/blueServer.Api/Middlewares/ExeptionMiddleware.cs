using System.Text.Json;
using blueServer.Api.Exceptions;
using blueServer.Infrastructure.Observability;

namespace blueServer.Api.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            _logger.LogDebug(
                LogEventIds.Api.HttpRequestCancelled,
                "HTTP request cancelled by client. Method={Method}, Path={Path}, RequestId={RequestId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            // 필수 본문 누락·잘못된 JSON 등의 입력 오류를 500으로 바꾸지 않음
            _logger.LogWarning(
                "Invalid HTTP request. Method={Method}, Path={Path}, RequestId={RequestId}, StatusCode={StatusCode}",
                context.Request.Method, context.Request.Path.Value,
                context.TraceIdentifier, ex.StatusCode);

            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(
                new { message = "Invalid HTTP request." }, context.RequestAborted);
        }
        catch (GameException ex)
        {
            _logger.LogWarning(
                LogEventIds.Api.GameRequestFailed,
                ex,
                "Game request failed. Method={Method}, Path={Path}, RequestId={RequestId}, Reason={Reason}",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier,
                ex.Message);

            context.Response.StatusCode = 400;

            context.Response.ContentType = "application/json";

            var response = new
            {
                message = ex.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                LogEventIds.Api.UnhandledRequestException,
                ex,
                "Unhandled request exception. Method={Method}, Path={Path}, RequestId={RequestId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);

            context.Response.StatusCode = 500;

            context.Response.ContentType = "application/json";

            var response = new
            {
                message = "Internal Server Error"
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
