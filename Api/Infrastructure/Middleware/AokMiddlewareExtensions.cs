using Api.Models.Owners;

namespace Api.Infrastructure.Middleware;

public static class AokMiddlewareExtensions
{
    /// <summary>
    /// Adds the AOK middleware to the application pipeline.
    /// This middleware transparently provisions anonymous owners and sets cookies.
    /// </summary>
    public static IApplicationBuilder UseAokMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AokMiddleware>();
    }

    /// <summary>
    /// Gets the current Owner from HttpContext (set by AokMiddleware).
    /// Throws if Owner is not present (middleware not configured properly).
    /// </summary>
    public static Owner GetOwner(this HttpContext context)
    {
        if (context.Items.TryGetValue("Owner", out var obj) && obj is Owner owner)
        {
            return owner;
        }
        throw new InvalidOperationException("Owner not found in HttpContext. Ensure AokMiddleware is configured.");
    }

    /// <summary>
    /// Tries to get the current Owner from HttpContext.
    /// Returns null if not present.
    /// </summary>
    public static Owner? TryGetOwner(this HttpContext context)
    {
        return context.Items.TryGetValue("Owner", out var obj) && obj is Owner owner
            ? owner
            : null;
    }
}

