namespace Overseer.Api.Common;

public sealed class ApiKeyAuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
    {
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/api/public"))
        {
            await next(context);
            return;
        }

        var headerName = configuration["Security:ApiKeyHeader"] ?? "X-Overseer-ApiKey";

        if (!context.Request.Headers.TryGetValue(headerName, out var apiKeyValue) || string.IsNullOrWhiteSpace(apiKeyValue))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing API key." });
            return;
        }

        var client = await apiKeyService.FindClientAsync(apiKeyValue.ToString(), context.RequestAborted);

        if (client is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
            return;
        }

        context.Items["TenantId"] = client.TenantId;
        await next(context);
    }
}
