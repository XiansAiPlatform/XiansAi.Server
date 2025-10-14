using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RestSharp;
using JwtClaims = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace Shared.Providers.Auth.GitHub;

/// <summary>
/// Service for handling GitHub OAuth token exchange and JWT minting
/// </summary>
public class GitHubTokenService : ITokenService
{
    private readonly ILogger<GitHubTokenService> _logger;
    private readonly GitHubConfig _config;
    private readonly HttpClient _httpClient;
    private readonly RestClient _restClient;
    private SecurityKey? _signingKey;

    public GitHubTokenService(
        ILogger<GitHubTokenService> logger,
        IConfiguration configuration,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _restClient = new RestClient();

        _config = configuration.GetSection("GitHub").Get<GitHubConfig>()
            ?? throw new ArgumentException("GitHub configuration is missing");

        ValidateConfiguration();
        InitializeSigningKey();
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(_config.ClientId))
            throw new ArgumentException("GitHub ClientId is required");
        if (string.IsNullOrEmpty(_config.ClientSecret))
            throw new ArgumentException("GitHub ClientSecret is required");
        if (string.IsNullOrEmpty(_config.JwtIssuer))
            throw new ArgumentException("GitHub JwtIssuer is required");
        if (string.IsNullOrEmpty(_config.JwtAudience))
            throw new ArgumentException("GitHub JwtAudience is required");

        // Ensure at least one signing method is configured
        if (string.IsNullOrEmpty(_config.JwtPrivateKeyPem) && string.IsNullOrEmpty(_config.JwtHmacSecret))
            throw new ArgumentException("Either GitHub JwtPrivateKeyPem or JwtHmacSecret must be configured");
    }

    private void InitializeSigningKey()
    {
        // Prefer asymmetric (RSA) signing
        if (!string.IsNullOrEmpty(_config.JwtPrivateKeyPem))
        {
            try
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(_config.JwtPrivateKeyPem);
                _signingKey = new RsaSecurityKey(rsa)
                {
                    KeyId = _config.JwtKeyId ?? "github-key-1"
                };
                _logger.LogInformation("Initialized RSA signing key for GitHub JWT minting");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse RSA private key from PEM");
                throw new ArgumentException("Invalid GitHub JwtPrivateKeyPem format", ex);
            }
        }
        // Fallback to symmetric (HMAC) signing
        else if (!string.IsNullOrEmpty(_config.JwtHmacSecret))
        {
            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.JwtHmacSecret));
            _logger.LogWarning("Using HMAC secret for GitHub JWT signing. RSA is recommended for production.");
        }
    }

    /// <summary>
    /// Exchange GitHub authorization code for our JWT
    /// </summary>
    public async Task<string> ExchangeCodeForJwt(string code, string redirectUri)
    {
        try
        {
            // Step 1: Exchange code for GitHub access token
            var githubAccessToken = await ExchangeCodeForGitHubToken(code, redirectUri);

            // Step 2: Fetch GitHub user profile
            var githubUser = await GetGitHubUser(githubAccessToken);

            // Step 3: Optionally fetch primary email if not in profile
            if (string.IsNullOrEmpty(githubUser.Email))
            {
                githubUser.Email = await GetPrimaryEmail(githubAccessToken);
            }

            // Step 4: Mint our own JWT
            var jwt = CreateJwt(githubUser);

            return jwt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange GitHub code for JWT");
            throw;
        }
    }

    private async Task<string> ExchangeCodeForGitHubToken(string code, string redirectUri)
    {
        try
        {
            var request = new RestRequest("https://github.com/login/oauth/access_token", Method.Post);
            request.AddHeader("Accept", "application/json");
            request.AddParameter("client_id", _config.ClientId);
            request.AddParameter("client_secret", _config.ClientSecret);
            request.AddParameter("code", code);
            request.AddParameter("redirect_uri", redirectUri);

            var response = await _restClient.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                _logger.LogError("GitHub token exchange failed: {StatusCode} - {Content}",
                    response.StatusCode, response.Content);
                throw new Exception($"GitHub token exchange failed: {response.ErrorMessage}");
            }

            var tokenResponse = JsonSerializer.Deserialize<GitHubTokenResponse>(response.Content!);
            if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
            {
                throw new Exception("GitHub did not return an access token");
            }

            _logger.LogDebug("Successfully exchanged code for GitHub access token");
            return tokenResponse.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging code for GitHub token");
            throw;
        }
    }

    private async Task<GitHubUser> GetGitHubUser(string accessToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("User-Agent", "XiansAI-Server");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<GitHubUser>(content);

            if (user == null)
            {
                throw new Exception("Failed to deserialize GitHub user");
            }

            _logger.LogDebug("Fetched GitHub user: {Login} (ID: {Id})", user.Login, user.Id);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching GitHub user profile");
            throw;
        }
    }

    private async Task<string?> GetPrimaryEmail(string accessToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("User-Agent", "XiansAI-Server");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var emails = JsonSerializer.Deserialize<List<GitHubEmail>>(content);

            var primaryEmail = emails?.FirstOrDefault(e => e.Primary && e.Verified)?.Email;
            if (!string.IsNullOrEmpty(primaryEmail))
            {
                _logger.LogDebug("Found primary verified email: {Email}", primaryEmail);
            }

            return primaryEmail;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch GitHub user emails");
            return null;
        }
    }

    private string CreateJwt(GitHubUser githubUser)
    {
        try
        {
            var claims = new List<Claim>
            {
                new Claim(JwtClaims.Sub, githubUser.Id.ToString()),
                new Claim("preferred_username", githubUser.Login ?? ""),
                new Claim(JwtClaims.Jti, Guid.NewGuid().ToString())
            };

            if (!string.IsNullOrEmpty(githubUser.Name))
            {
                claims.Add(new Claim(JwtClaims.Name, githubUser.Name));
            }

            if (!string.IsNullOrEmpty(githubUser.Email))
            {
                claims.Add(new Claim(JwtClaims.Email, githubUser.Email));
            }

            var credentials = _signingKey switch
            {
                RsaSecurityKey rsa => new SigningCredentials(rsa, SecurityAlgorithms.RsaSha256),
                SymmetricSecurityKey symmetric => new SigningCredentials(symmetric, SecurityAlgorithms.HmacSha256),
                _ => throw new InvalidOperationException("No signing key configured")
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = _config.JwtIssuer,
                Audience = _config.JwtAudience,
                Expires = DateTime.UtcNow.AddMinutes(_config.JwtAccessTokenMinutes),
                IssuedAt = DateTime.UtcNow,
                SigningCredentials = credentials
            };

            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateToken(tokenDescriptor);
            var jwt = handler.WriteToken(token);

            _logger.LogDebug("Created JWT for GitHub user {Login} with expiry {Expiry}",
                githubUser.Login, tokenDescriptor.Expires);

            return jwt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating JWT for GitHub user");
            throw;
        }
    }

    /// <summary>
    /// Validate a JWT token that we issued
    /// </summary>
    public async Task<(bool success, string? userId)> ProcessToken(string token)
    {
        try
        {
            var handler = new JsonWebTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _config.JwtIssuer,

                ValidateAudience = true,
                ValidAudience = _config.JwtAudience,

                ValidateLifetime = true,
                RequireExpirationTime = true,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,

                ClockSkew = TimeSpan.FromMinutes(5),
                NameClaimType = "sub",
                RoleClaimType = ClaimTypes.Role
            };

            var result = await handler.ValidateTokenAsync(token, validationParameters);

            if (!result.IsValid)
            {
                _logger.LogWarning("JWT validation failed: {Error}",
                    result.Exception?.Message ?? "Unknown error");
                return (false, null);
            }

            var userId = result.ClaimsIdentity.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No 'sub' claim found in validated token");
                return (false, null);
            }

            _logger.LogDebug("Successfully validated JWT for user {UserId}", userId);
            return (true, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token");
            return (false, null);
        }
    }

    public string? ExtractUserId(JwtSecurityToken token)
    {
        return token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
    }

    public IEnumerable<string> ExtractTenantIds(JwtSecurityToken token)
    {
        // GitHub tokens don't contain tenant info by default
        // Could be added via custom claims if needed
        return Enumerable.Empty<string>();
    }

    public Task<string> GetManagementApiToken()
    {
        // GitHub provider doesn't use a management API token
        // User info is fetched during the OAuth exchange
        throw new NotImplementedException("GitHub provider does not use management API tokens");
    }
}

