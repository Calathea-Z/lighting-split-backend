namespace Api.Infrastructure.Middleware;

/// <summary>
/// Middleware to add security headers for production environments.
/// Phase 3: HTTPS and CORS alignment.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only add strict security headers in production
        if (!_env.IsDevelopment())
        {
            // Prevent MIME-type sniffing
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            // Enable browser XSS protection
            context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

            // Prevent clickjacking (allow same-origin for iframes if needed)
            context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

            // Content Security Policy (adjust as needed for your app)
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data:; " +
                "connect-src 'self';";

            // Referrer policy
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Permissions policy (restrict features)
            context.Response.Headers["Permissions-Policy"] =
                "geolocation=(), microphone=(), camera=()";
        }

        await _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}

