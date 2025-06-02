using Microsoft.Extensions.Caching.Distributed;
using Features.WebApi.Auth;
using Features.WebApi.Models;
using Shared.Auth;
using Shared.Utils.Services;
using Shared.Utils;
using XiansAi.Server.Utils;
using Shared.Services;

namespace Features.WebApi.Services;

/// <summary>
/// Interface for user registration and email verification processes.
/// </summary>
public interface IPublicService
{
    /// <summary>
    /// Validates a verification code for the specified email.
    /// </summary>
    /// <param name="email">The email address to validate the code for</param>
    /// <param name="code">The verification code to validate</param>
    /// <returns>True if the code is valid, false otherwise</returns>
    Task<bool> ValidateCode(string email, string code);

    /// <summary>
    /// Sends a verification code to the specified email address.
    /// </summary>
    /// <param name="email">The email address to send the verification code to</param>
    /// <returns>A ServiceResult indicating success or failure</returns>
    Task<ServiceResult<SendVerificationCodeResult>> SendVerificationCode(string email);
}

/// <summary>
/// Handles user registration and email verification processes.
/// </summary>
public class PublicService : IPublicService
{
    private readonly IAuthMgtConnect _authMgtConnect;
    private readonly ILogger<PublicService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly ObjectCache _cache;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly Random _random;
    
    // Constants for configuration
    private const int CODE_EXPIRATION_MINUTES = 15;
    private const string VERIFICATION_CACHE_PREFIX = "verification:";
    private const string EMAIL_SUBJECT = "Xians.ai - Verification Code";

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicService"/> class.
    /// </summary>
    /// <param name="authMgtConnect">Service for Auth0 Management API operations</param>
    /// <param name="logger">Logger for this class</param>
    /// <param name="tenantContext">Context for tenant operations</param>
    /// <param name="cache">Object cache service for storing verification codes</param>
    /// <param name="emailService">Service for sending emails</param>
    /// <param name="configuration">Application configuration</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null</exception>
    public PublicService(
        IAuthMgtConnect authMgtConnect, 
        ILogger<PublicService> logger,
        ITenantContext tenantContext,
        ObjectCache cache, 
        IEmailService emailService,
        IConfiguration configuration)
    {
        _authMgtConnect = authMgtConnect ?? throw new ArgumentNullException(nameof(authMgtConnect));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _random = new Random();
    }

    /// <summary>
    /// Validates a verification code for the specified email.
    /// </summary>
    /// <param name="email">The email address to validate the code for</param>
    /// <param name="code">The verification code to validate</param>
    /// <returns>True if the code is valid, false otherwise</returns>
    /// <exception cref="ArgumentException">Thrown when email or code is null or empty</exception>
    public async Task<bool> ValidateCode(string email, string code)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
            
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code cannot be null or empty", nameof(code));
        
        _logger.LogDebug("Validating code for email: {Email}", email);    
        return await ValidateCodeAsync(email, code);
    }

    /// <summary>
    /// Sends a verification code to the specified email address.
    /// </summary>
    /// <param name="email">The email address to send the verification code to</param>
    /// <returns>A ServiceResult indicating success or failure</returns>
    public async Task<ServiceResult<SendVerificationCodeResult>> SendVerificationCode(string email)
    {
        _logger.LogInformation("Received request to send verification code to email: {Email}", email);
        
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("SendVerificationCode failed: Email is empty or null");
            return ServiceResult<SendVerificationCodeResult>.BadRequest("Email is required");
        }

        try
        {
            // Extract tenant ID from email domain
            var tenantId = GenerateTenantId(email);
            _logger.LogDebug("Generated tenant ID: {TenantId} from email: {Email}", tenantId, email);

            // Validate if the tenant ID is registered in our system
            if (!IsValidTenantId(tenantId))
            {
                _logger.LogWarning("Invalid tenant ID: {TenantId} for email: {Email}", tenantId, email);
                return ServiceResult<SendVerificationCodeResult>.BadRequest("This email domain is not registered with Xians.ai. Please contact Xians.ai support to get access to the platform.");
            }

            // Generate and send verification code
            _logger.LogInformation("Sending verification code to email: {Email}", email);
            var code = await GenerateCodeAsync(email);

            // Send email with verification code
            await _emailService.SendEmailAsync(email, EMAIL_SUBJECT, GetEmailBody(code), false);
            _logger.LogInformation("##### Verification code {Code} sent successfully to: {Email}", code, email);

            return ServiceResult<SendVerificationCodeResult>.Success(new SendVerificationCodeResult 
            { 
                Message = $"Verification code sent to {email}" 
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument when sending verification code to {Email}: {Message}", email, ex.Message);
            return ServiceResult<SendVerificationCodeResult>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when sending verification code to {Email}: {Message}", email, ex.Message);
            return ServiceResult<SendVerificationCodeResult>.InternalServerError("An error occurred while sending verification code. Error: " + ex.Message);
        }
    }

    /// <summary>
    /// Generates a random verification code and stores it in the cache.
    /// </summary>
    /// <param name="email">The email address to generate a code for</param>
    /// <returns>The generated verification code</returns>
    /// <exception cref="ArgumentException">Thrown when the email domain is not valid</exception>
    private async Task<string> GenerateCodeAsync(string email)
    {
        // Extract tenant ID from email domain
        var tenantId = GenerateTenantId(email);
        
        // Double-check tenant validity
        if (!IsValidTenantId(tenantId))
        {
            _logger.LogWarning("Attempted to generate code for invalid tenant: {TenantId}", tenantId);
            throw new ArgumentException($"Email domain does not belong to a valid tenant. Please contact Xians.ai support to get access to the platform.");
        }

        // Generate a 6-digit code
        string code = _random.Next(100000, 999999).ToString();
        _logger.LogDebug("Generated verification code for {Email}", email);
        
        // Store in cache with expiration
        string cacheKey = GetVerificationCacheKey(email);
        await _cache.SetAsync(cacheKey, code, TimeSpan.FromMinutes(CODE_EXPIRATION_MINUTES));
        _logger.LogDebug($"Stored verification code {code} in cache with key: {cacheKey}, expiration: {CODE_EXPIRATION_MINUTES} minutes");

        return code;
    }

    /// <summary>
    /// Checks if the tenant ID is valid by looking it up in configuration.
    /// </summary>
    /// <param name="tenantId">The tenant ID to validate</param>
    /// <returns>True if the tenant ID is valid, false otherwise</returns>
    private bool IsValidTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogDebug("Tenant ID validation failed: tenantId is null or empty");
            return false;
        }
            
        _logger.LogDebug("Starting tenant validation for ID: {TenantId}", tenantId);
            
        // Try different formats and log the results
        var formats = new[]
        {
            $"Tenants:{tenantId}:Enabled",              // JSON format with Enabled (this is the one that exists)
            $"Tenants__{tenantId}__Enabled",           // Azure env var format
            $"Tenants:{tenantId}",                     // Simple JSON format
            $"Tenants__{tenantId}"                     // Simple env var format
        };

        foreach (var format in formats)
        {
            // Try both direct access and section access
            var directValue = _configuration[format];
            var section = _configuration.GetSection(format);
            
            _logger.LogDebug("Configuration check - Format: {Format}", format);
            _logger.LogDebug("  Direct access - Value: {Value}, Type: {Type}", 
                directValue ?? "null",
                directValue?.GetType().Name ?? "null");
            _logger.LogDebug("  Section access - Exists: {Exists}, Value: {Value}, Type: {Type}", 
                section.Exists(),
                section.Value ?? "null",
                section.Value?.GetType().Name ?? "null");
            
            // Try to get the value from either source
            var value = directValue ?? section.Value;
            
            if (!string.IsNullOrEmpty(value))
            {
                _logger.LogDebug("Attempting to parse value '{Value}' as boolean", value);
                if (bool.TryParse(value, out var isEnabled))
                {
                    _logger.LogDebug("Successfully parsed tenant configuration - Format: {Format}, Value: {Value}, IsEnabled: {IsEnabled}", 
                        format, value, isEnabled);
                    return isEnabled;
                }
                else
                {
                    _logger.LogDebug("Failed to parse value '{Value}' as boolean for format {Format}", 
                        value, format);
                }
            }
        }
        
        // Log all configuration keys for debugging
        var allKeys = _configuration.AsEnumerable()
            .Where(x => x.Key.StartsWith("Tenants"))
            .Select(x => $"{x.Key} = {x.Value}");
        _logger.LogDebug("All tenant-related configuration keys: {Keys}", 
            string.Join(", ", allKeys));
        
        // Try one last time with the exact format we see in the configuration
        var finalCheck = _configuration[$"Tenants:{tenantId}:Enabled"];
        if (!string.IsNullOrEmpty(finalCheck))
        {
            _logger.LogDebug("Found configuration with exact format - Value: {Value}", finalCheck);
            if (bool.TryParse(finalCheck, out var isEnabled))
            {
                _logger.LogDebug("Successfully parsed final check - Value: {Value}, IsEnabled: {IsEnabled}", 
                    finalCheck, isEnabled);
                return isEnabled;
            }
        }
        
        _logger.LogDebug("Tenant ID {TenantId} not found in any configuration format", tenantId);
        return false;
    }

    /// <summary>
    /// Generates the email body containing the verification code.
    /// </summary>
    /// <param name="code">The verification code to include in the email</param>
    /// <returns>The formatted email body</returns>
    private string GetEmailBody(string code)
    {
        return $@"Hi there,

Your Xians.ai verification code is: {code}

This code expires in {CODE_EXPIRATION_MINUTES} minutes.

Best regards,
The Xians.ai Team";
    }

    /// <summary>
    /// Validates a verification code against the stored code for the email.
    /// If valid, updates the tenant information in Auth0.
    /// </summary>
    /// <param name="email">The email address to validate the code for</param>
    /// <param name="code">The verification code to validate</param>
    /// <returns>True if the code is valid, false otherwise</returns>
    private async Task<bool> ValidateCodeAsync(string email, string code)
    {
        _logger.LogInformation("Validating verification code for email: {Email}", email);

        // Retrieve stored code from cache
        string cacheKey = GetVerificationCacheKey(email);
        string? storedCode = await _cache.GetAsync<string>(cacheKey);

        // Check if code exists
        if (string.IsNullOrEmpty(storedCode))
        {
            _logger.LogWarning("No verification code found for email: {Email}. Code may have expired.", email);
            return false;
        }

        // Compare codes
        bool isValid = storedCode == code;
        
        if (isValid)
        {
            _logger.LogInformation("Verification code validated successfully for email: {Email}", email);
            
            try
            {
                // Remove the code after successful validation
                await _cache.RemoveAsync(cacheKey);
                _logger.LogDebug("Removed verification code from cache for email: {Email}", email);

                // Set the tenant information in the Auth0 user
                var user = _tenantContext.LoggedInUser ?? 
                    throw new UnauthorizedAccessException("User ID not found");
                    
                var tenantId = GenerateTenantId(email);
                _logger.LogDebug("Setting tenant {TenantId} for user {UserId}", tenantId, user);
                
                await _authMgtConnect.SetNewTenant(user, tenantId);
                _logger.LogInformation("Successfully set tenant {TenantId} for user {UserId}", tenantId, user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during post-validation processing for email: {Email}", email);
                throw; // Re-throw to let caller handle it
            }
        }
        else
        {
            _logger.LogWarning("Invalid verification code provided for email: {Email}", email);
        }
        
        return isValid;
    }

    /// <summary>
    /// Extracts the tenant ID from an email address by taking the domain part.
    /// </summary>
    /// <param name="email">The email address to extract the tenant ID from</param>
    /// <returns>The tenant ID derived from the email domain</returns>
    /// <exception cref="ArgumentException">Thrown when the email format is invalid</exception>
    private static string GenerateTenantId(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));
            
        // Extract domain part and remove all non-alphanumeric characters
        var domain = email.Split('@')[1];
        var tenantId = domain.ToLowerInvariant().Trim();
        return tenantId;
    }
    
    /// <summary>
    /// Generates a cache key for storing verification codes.
    /// </summary>
    /// <param name="email">The email address to generate a cache key for</param>
    /// <returns>The cache key for the email</returns>
    private static string GetVerificationCacheKey(string email) => $"{VERIFICATION_CACHE_PREFIX}{email}";
}
