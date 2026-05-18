using System.Text.Json;
using blueServer.Api.Exceptions;

namespace blueServer.Api.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (GameException ex)
        {
            context.Response.StatusCode = 400;

            context.Response.ContentType = "application/json";

            var response = new
            {
                message = ex.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception)
        {
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