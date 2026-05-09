
using System.Net;
using System.Text.Json;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate                    _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate                    next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt on {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteErrorResponse(context, HttpStatusCode.Unauthorized, "Unauthorized.");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument on {Method} {Path}: {Message}",
                context.Request.Method, context.Request.Path, ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation on {Method} {Path}: {Message}",
                context.Request.Method, context.Request.Path, ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError,
                "An unexpected error occurred. Please try again later.");
        }
    }

    private static async Task WriteErrorResponse(
        HttpContext  context,
        HttpStatusCode status,
        string message)
    {
        // Guard: if response has already started (e.g. streaming), we cannot write headers
        if (context.Response.HasStarted)
            return;

        context.Response.ContentType = "application/json";
        context.Response.StatusCode  = (int)status;

        var response = new ApiResponse<object>
        {
            Success = false,
            Message = message,
            Data    = null
        };

        var json = JsonSerializer.Serialize(response,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        await context.Response.WriteAsync(json);
    }
}