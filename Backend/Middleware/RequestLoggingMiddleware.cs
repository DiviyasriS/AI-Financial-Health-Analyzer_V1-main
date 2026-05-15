using System.Diagnostics;
using System.Security.Claims;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            int statusCode = context.Response.StatusCode;

            if (statusCode >= StatusCodes.Status400BadRequest)
            {
                string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? context.User.FindFirstValue("userId");

                _logger.LogWarning(
                    "Failed request {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds}ms for UserId={UserId}",
                    context.Request.Method,
                    context.Request.Path,
                    statusCode,
                    stopwatch.ElapsedMilliseconds,
                    userId ?? "Anonymous");
            }
        }
    }
}