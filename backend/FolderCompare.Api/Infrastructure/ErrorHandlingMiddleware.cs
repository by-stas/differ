using System.Text.Json;
using FolderCompare.Api.Models;

namespace FolderCompare.Api.Infrastructure;

/// <summary>Translates exceptions into the structured <see cref="ApiError"/> contract.</summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
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
        catch (ComparisonException ex)
        {
            _logger.LogWarning("{Code}: {Message}", ex.Code, ex.Message);
            await WriteAsync(context, ex.StatusCode, ex.ToApiError());
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing useful can be written to the response.
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied while handling {Path}", context.Request.Path);
            await WriteAsync(context, StatusCodes.Status403Forbidden, new ApiError(ErrorCodes.AccessDenied, "Access to the requested path was denied."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while handling {Path}", context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, new ApiError(ErrorCodes.InternalError, "An unexpected error occurred."));
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, ApiError error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(error, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
