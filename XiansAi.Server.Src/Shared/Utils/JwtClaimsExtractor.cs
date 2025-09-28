using System.IdentityModel.Tokens.Jwt;
using Shared.Providers.Auth;

namespace Shared.Utils;

/// <summary>
/// Utility class for secure JWT token validation and claims extraction
/// Provides a centralized, consistent approach to JWT handling across the application
/// </summary>
public interface IJwtClaimsExtractor
{
    /// <summary>
    /// Validates a JWT token and extracts user information
    /// </summary>
    /// <param name="token">The JWT token to validate and process</param>
    /// <returns>Validation result with extracted user information</returns>
    Task<JwtValidationResult> ValidateAndExtractClaimsAsync(string token);

    /// <summary>
    /// Extracts specific claim value from an already validated token
    /// </summary>
    /// <param name="token">The validated JWT token</param>
    /// <param name="claimType">The claim type to extract</param>
    /// <param name="fallbackClaimTypes">Alternative claim types to try if primary fails</param>
    /// <returns>The claim value or null if not found</returns>
    string? ExtractClaim(string token, string claimType, params string[] fallbackClaimTypes);

    /// <summary>
    /// Extracts user ID from token using standard claim hierarchy
    /// </summary>
    /// <param name="token">The validated JWT token</param>
    /// <returns>User ID or null if not found</returns>
    string? ExtractUserId(string token);

    /// <summary>
    /// Extracts email from token using standard claim hierarchy
    /// </summary>
    /// <param name="token">The validated JWT token</param>
    /// <returns>Email or null if not found</returns>
    string? ExtractEmail(string token);

    /// <summary>
    /// Extracts display name from token
    /// </summary>
    /// <param name="token">The validated JWT token</param>
    /// <returns>Display name or null if not found</returns>
    string? ExtractName(string token);
}

/// <summary>
/// Result of JWT token validation and claims extraction
/// </summary>
public class JwtValidationResult
{
    public bool IsValid { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? ErrorMessage { get; set; }
    
    public static JwtValidationResult Success(string userId, string? email = null, string? name = null)
    {
        return new JwtValidationResult
        {
            IsValid = true,
            UserId = userId,
            Email = email,
            Name = name
        };
    }
    
    public static JwtValidationResult Failure(string errorMessage)
    {
        return new JwtValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Implementation of JWT claims extractor with secure validation
/// </summary>
public class JwtClaimsExtractor : IJwtClaimsExtractor
{
    private readonly IAuthProviderFactory _authProviderFactory;
    private readonly ILogger<JwtClaimsExtractor> _logger;

    // Standard claim type constants for consistency
    private static readonly string[] UserIdClaimTypes = { "sub", "preferred_username", "user_id", "uid", "id", "email", "username" };
    private static readonly string[] EmailClaimTypes = { "email", "upn" };
    private static readonly string[] NameClaimTypes = { "name", "given_name", "display_name" };

    public JwtClaimsExtractor(IAuthProviderFactory authProviderFactory, ILogger<JwtClaimsExtractor> logger)
    {
        _authProviderFactory = authProviderFactory;
        _logger = logger;
    }

    /// <summary>
    /// Validates JWT token with JWKS and extracts common user claims
    /// </summary>
    public async Task<JwtValidationResult> ValidateAndExtractClaimsAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return JwtValidationResult.Failure("Token cannot be null or empty");
        }

        try
        {
            // Step 1: Validate token signature with auth provider (JWKS validation)
            var authProvider = _authProviderFactory.GetProvider();
            var (isValid, validatedUserId) = await authProvider.ValidateToken(token);

            if (!isValid || string.IsNullOrEmpty(validatedUserId))
            {
                _logger.LogWarning("JWT token validation failed through auth provider");
                return JwtValidationResult.Failure("Token validation failed");
            }

            // Step 2: Extract additional claims from the validated token
            var email = ExtractEmail(token);
            var name = ExtractName(token);

            _logger.LogDebug("Successfully validated and extracted claims for user: {UserId}", validatedUserId);
            
            return JwtValidationResult.Success(validatedUserId, email, name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token and extracting claims");
            return JwtValidationResult.Failure("Token processing failed");
        }
    }

    /// <summary>
    /// Extracts a specific claim with fallback options
    /// </summary>
    public string? ExtractClaim(string token, string claimType, params string[] fallbackClaimTypes)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format during claim extraction");
                return null;
            }

            // Try primary claim type first
            var claimValue = jsonToken.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
            if (!string.IsNullOrEmpty(claimValue))
            {
                _logger.LogDebug("Found claim '{ClaimType}': {Value}", claimType, claimValue);
                return claimValue;
            }

            // Try fallback claim types
            foreach (var fallbackType in fallbackClaimTypes)
            {
                claimValue = jsonToken.Claims.FirstOrDefault(c => c.Type == fallbackType)?.Value;
                if (!string.IsNullOrEmpty(claimValue))
                {
                    _logger.LogDebug("Found claim '{FallbackType}' (fallback for '{ClaimType}'): {Value}", 
                        fallbackType, claimType, claimValue);
                    return claimValue;
                }
            }

            _logger.LogDebug("Claim '{ClaimType}' not found in token (tried {FallbackCount} fallbacks)", 
                claimType, fallbackClaimTypes.Length);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting claim '{ClaimType}' from token", claimType);
            return null;
        }
    }

    /// <summary>
    /// Extracts user ID using standard claim hierarchy
    /// Follows the same pattern as existing auth providers
    /// </summary>
    public string? ExtractUserId(string token)
    {
        var userId = ExtractClaim(token, UserIdClaimTypes[0], UserIdClaimTypes.Skip(1).ToArray());
        
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in any known claim types");
        }
        
        return userId;
    }

    /// <summary>
    /// Extracts email using standard claim hierarchy
    /// </summary>
    public string? ExtractEmail(string token)
    {
        return ExtractClaim(token, EmailClaimTypes[0], EmailClaimTypes.Skip(1).ToArray());
    }

    /// <summary>
    /// Extracts display name using standard claim hierarchy
    /// </summary>
    public string? ExtractName(string token)
    {
        return ExtractClaim(token, NameClaimTypes[0], NameClaimTypes.Skip(1).ToArray());
    }
}
