using Api.Models.Owners;
using Api.Services.Payments.Abstractions;

namespace Api.Infrastructure.Middleware;

/// <summary>
/// Middleware that transparently provisions anonymous owners and sets cookies.
/// Phase 1: Auto-provision on every request if no valid token exists.
/// </summary>
public class AokMiddleware
{
    private readonly RequestDelegate _next;

    public AokMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context, IAokService aok)
    {
        if (aok is null) throw new ArgumentNullException(nameof(aok));

        // Resolve or auto-provision owner
        var (owner, rawToken) = await aok.ResolveOrProvisionOwnerAsync(context);

        // If owner is null, it means the token was tampered with
        if (owner is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or tampered authentication token" });
            return;
        }

        // Store owner in HttpContext for controllers
        context.Items["Owner"] = owner;

        // If a new token was created, set the cookie
        if (rawToken is not null)
        {
            aok.SetAokCookie(context.Response, rawToken);
        }

        // Continue pipeline
        await _next(context);
    }
}

