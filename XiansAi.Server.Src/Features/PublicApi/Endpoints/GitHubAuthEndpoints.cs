using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Shared.Providers.Auth.GitHub;
using Shared.Services;

namespace Features.PublicApi.Endpoints;

/// <summary>
/// GitHub OAuth authentication endpoints
/// </summary>
public static class GitHubAuthEndpoints
{
    public static void MapGitHubAuthEndpoints(this WebApplication app)
    {
        var githubGroup = app.MapGroup("/api/public/auth/github")
            .WithTags("PublicAPI - GitHub Auth");

        githubGroup.MapPost("/callback", async (
            [FromBody] GitHubCallbackDto dto,
            [FromServices] IConfiguration config,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] IJwtIssuer jwtIssuer,
            [FromServices] ILogger<GitHubCallbackDto> logger) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Code))
                {
                    return Results.BadRequest(new { error = "Missing code parameter" });
                }

                var clientId = config["GitHubOAuth:ClientId"];
                var clientSecret = config["GitHubOAuth:ClientSecret"];

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    logger.LogError("GitHub OAuth configuration is missing");
                    return Results.Problem("GitHub authentication is not properly configured");
                }

                // 1) Exchange code for access token
                var http = httpClientFactory.CreateClient();
                var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
                tokenReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["code"] = dto.Code,
                    ["redirect_uri"] = dto.RedirectUri ?? string.Empty
                });

                var tokenResp = await http.SendAsync(tokenReq);
                if (!tokenResp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Failed to exchange GitHub code for token: {StatusCode}", tokenResp.StatusCode);
                    return Results.Unauthorized();
                }

                var tokenJson = await tokenResp.Content.ReadFromJsonAsync<GitHubTokenResponse>();
                if (string.IsNullOrWhiteSpace(tokenJson?.AccessToken))
                {
                    logger.LogWarning("GitHub token response missing access_token");
                    return Results.Unauthorized();
                }

                // 2) Fetch GitHub user information
                var ghClient = httpClientFactory.CreateClient();
                ghClient.DefaultRequestHeaders.UserAgent.ParseAdd("XiansAI-GitHub-SSO/1.0");
                ghClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenJson.AccessToken);
                
                var user = await ghClient.GetFromJsonAsync<GitHubUser>("https://api.github.com/user");
                if (user?.Login == null)
                {
                    logger.LogWarning("Failed to fetch GitHub user information");
                    return Results.Unauthorized();
                }

                // 3) Mint first-party JWT
                var subject = $"github|{user.Login}";
                var claims = new List<Claim>
                {
                    new Claim(JwtRegisteredClaimNames.Sub, subject),
                    new Claim("provider", "github"),
                    new Claim("login", user.Login),
                    new Claim(ClaimTypes.Name, user.Name ?? user.Login)
                };

                if (!string.IsNullOrEmpty(user.Email))
                {
                    claims.Add(new Claim(ClaimTypes.Email, user.Email));
                }

                var token = jwtIssuer.Issue(claims);
                
                logger.LogInformation("Successfully authenticated GitHub user: {Login}", user.Login);
                
                return Results.Ok(new { token });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing GitHub callback");
                return Results.Problem("An error occurred during GitHub authentication");
            }
        })
        .WithName("GitHub OAuth Callback")
        .WithOpenApi(operation =>
        {
            operation.Summary = "GitHub OAuth callback";
            operation.Description = "Exchange GitHub authorization code for a JWT token. This endpoint is called by the UI after the user authorizes the application on GitHub.";
            operation.RequestBody.Description = "GitHub callback data containing the authorization code and redirect URI";
            return operation;
        });
    }
}

