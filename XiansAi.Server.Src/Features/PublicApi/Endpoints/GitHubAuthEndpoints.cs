using Microsoft.AspNetCore.Mvc;
using Shared.Providers.Auth.GitHub;
using System.ComponentModel.DataAnnotations;

namespace Features.PublicApi.Endpoints;

/// <summary>
/// GitHub OAuth code exchange request
/// </summary>
public class GitHubExchangeRequest
{
    /// <summary>
    /// Authorization code from GitHub OAuth callback
    /// </summary>
    [Required]
    public string Code { get; set; } = default!;

    /// <summary>
    /// Redirect URI (must match GitHub OAuth app settings)
    /// </summary>
    [Required]
    public string RedirectUri { get; set; } = default!;
}

/// <summary>
/// JWT token response
/// </summary>
public class GitHubTokenResponse
{
    /// <summary>
    /// JWT access token for API authentication
    /// </summary>
    public string AccessToken { get; set; } = default!;

    /// <summary>
    /// Token type (always "Bearer")
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// Token expiration in seconds
    /// </summary>
    public int ExpiresIn { get; set; }
}

public static class GitHubAuthEndpoints
{
    public static void MapGitHubAuthEndpoints(this WebApplication app)
    {
        var githubGroup = app.MapGroup("/api/public/auth/github")
            .WithTags("PublicAPI - GitHub Authentication");

        // Exchange GitHub authorization code for JWT
        githubGroup.MapPost("/exchange", async (
            [FromBody] GitHubExchangeRequest request,
            [FromServices] GitHubTokenService tokenService,
            [FromServices] IConfiguration configuration,
            [FromServices] ILogger<GitHubExchangeRequest> logger) =>
        {
            try
            {
                // Validate request
                if (string.IsNullOrWhiteSpace(request.Code))
                {
                    return Results.BadRequest(new
                    {
                        error = "invalid_request",
                        error_description = "Code parameter is required"
                    });
                }

                if (string.IsNullOrWhiteSpace(request.RedirectUri))
                {
                    return Results.BadRequest(new
                    {
                        error = "invalid_request",
                        error_description = "RedirectUri parameter is required"
                    });
                }

                // Exchange code for JWT
                var jwt = await tokenService.ExchangeCodeForJwt(request.Code, request.RedirectUri);

                // Get token lifetime from configuration
                var config = configuration.GetSection("GitHub").Get<GitHubConfig>();
                var expiresIn = config?.JwtAccessTokenMinutes ?? 60;

                // Return JWT in OAuth2 format
                return Results.Ok(new GitHubTokenResponse
                {
                    AccessToken = jwt,
                    TokenType = "Bearer",
                    ExpiresIn = expiresIn * 60 // Convert minutes to seconds
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GitHub code exchange failed");
                return Results.BadRequest(new
                {
                    error = "exchange_failed",
                    error_description = "Failed to exchange GitHub authorization code. The code may be invalid or expired."
                });
            }
        })
        .WithName("Exchange GitHub Code for JWT")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Exchange GitHub authorization code for JWT";
            operation.Description = @"Exchange a GitHub OAuth authorization code for a JWT access token. 

**Flow:**
1. User authenticates with GitHub and is redirected to your app with a `code` parameter
2. Your frontend calls this endpoint with the `code` and `redirectUri`
3. Server exchanges the code with GitHub, fetches user info, and returns a JWT
4. Frontend uses the JWT for all subsequent API calls with `Authorization: Bearer <jwt>` header

**Rate limiting:** This endpoint is rate-limited to prevent abuse. Maximum 10 requests per minute per IP.";

            operation.RequestBody.Description = "GitHub authorization code and redirect URI";
            return operation;
        })
        .RequireRateLimiting("PublicApiRegistration"); // Reuse existing rate limit policy

        // Health check / configuration info endpoint
        githubGroup.MapGet("/config", (
            [FromServices] IConfiguration configuration) =>
        {
            var config = configuration.GetSection("GitHub").Get<GitHubConfig>();
            
            if (config == null || string.IsNullOrEmpty(config.ClientId))
            {
                return Results.Ok(new
                {
                    configured = false,
                    message = "GitHub authentication is not configured"
                });
            }

            return Results.Ok(new
            {
                configured = true,
                clientId = config.ClientId,
                redirectUri = config.RedirectUri,
                scopes = config.Scopes,
                authorizeUrl = "https://github.com/login/oauth/authorize",
                tokenLifetimeMinutes = config.JwtAccessTokenMinutes
            });
        })
        .WithName("Get GitHub OAuth Configuration")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get GitHub OAuth configuration";
            operation.Description = "Returns public GitHub OAuth configuration needed by the frontend to initiate the OAuth flow. No authentication required.";
            return operation;
        })
        .RequireRateLimiting("PublicApiGet"); // Reuse existing rate limit policy for GET requests
    }
}

