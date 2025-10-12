namespace Api.Options;

/// <summary>
/// Configuration options for frontend application settings
/// </summary>
public sealed class FrontendOptions
{
    /// <summary>
    /// Base URL of the frontend application (e.g., http://localhost:3000)
    /// Used for generating share URLs that point to the frontend, not the backend API
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";
}
