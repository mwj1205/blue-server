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
