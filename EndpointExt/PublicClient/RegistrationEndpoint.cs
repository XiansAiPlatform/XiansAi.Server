using Microsoft.Extensions.Caching.Distributed;
using XiansAi.Server.Auth;
using XiansAi.Server.Temporal;

public class RegistrationEndpoint 
{
    private readonly IAuth0MgtAPIConnect _auth0MgtAPIConnect;
    private readonly ILogger<RegistrationEndpoint> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly ITemporalClientService _temporalClientService;
    private readonly IDistributedCache _cache;
    private readonly Random _random;
    private readonly IEmailService _emailService;

    public RegistrationEndpoint(
        IAuth0MgtAPIConnect auth0MgtAPIConnect, 
        ITemporalClientService temporalClientService,
        ILogger<RegistrationEndpoint> logger,
        ITenantContext tenantContext,
        IDistributedCache cache, 
        IEmailService emailService)
    {
        _auth0MgtAPIConnect = auth0MgtAPIConnect;
        _logger = logger;
        _tenantContext = tenantContext;
        _temporalClientService = temporalClientService;

        _cache = cache;
        _random = new Random();
        _emailService = emailService;
    }

    public async Task<bool> ValidateCode(string email, string code)
    {
        var isValid = await ValidateCodeAsync(email, code);
        return isValid;
    }

    public async Task<IResult> SendVerificationCode(string email)
    {
        _logger.LogInformation($"Sending verification code to {email}");
        var code = await GenerateCodeAsync(email);

        await _emailService.SendEmailAsync(email, "Xians.ai - Verification Code", GetEmailBody(code), false);
        _logger.LogInformation($"Verification code generated for {email}: {code}");

        return Results.Ok($"Verification code sent to {email}");
    }


    private async Task<string> GenerateCodeAsync(string email)
    {
        // Generate a 6-digit code
        string code = _random.Next(100000, 999999).ToString();
        
        // Store in cache with 15 minutes expiration
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        };
        
        await _cache.SetStringAsync(
            $"verification:{email}", 
            code, 
            options
        );

        return code;
    }

    private string GetEmailBody(string code)
    {
        return $@"Hello,

Your verification code for Xians.ai is: {code}

This code will expire in 15 minutes. If you didn't request this code, please ignore this email.

Best regards,
The Xians.ai Team";
    }

    private async Task<bool> ValidateCodeAsync(string email, string code)
    {
        _logger.LogInformation($"Validating verification code for {email}: {code}");

        string? storedCode = await _cache.GetStringAsync($"verification:{email}");

        bool isValid = false;
        
        if (string.IsNullOrEmpty(storedCode))
        {
            isValid = false; // Code expired or doesn't exist
        }
        
        isValid = storedCode == code;
        
        if (isValid)
        {
            // Remove the code after successful validation
            await _cache.RemoveAsync($"verification:{email}");

            // Set the tenant information in the Auth0 user
            var user = _tenantContext.LoggedInUser ?? throw new UnauthorizedAccessException("User ID not found");
            var tenantId = GenerateTenantId(email);
            await _auth0MgtAPIConnect.SetNewTenant(user, tenantId);
        }

        _logger.LogInformation($"Verification code validated for {email}: {isValid}");
        
        return isValid;
    }

    private static string GenerateTenantId(string email)
    {
        // Extract domain part and remove all non-alphanumeric characters
        var domain = email.Split('@')[1];
        var tenantId = new string(domain.Where(c => char.IsLetterOrDigit(c)).ToArray());
        return tenantId;
    }
    
}

public class RegisterTenantRequest
{
    public required string CompanyEmail { get; set; }
    public required string CompanyUrl { get; set; }
    public required string TenantId { get; set; }
    public required string SubscriptionType { get; set; }
}

public class ValidateCodeRequest
{
    public required string Email { get; set; }
    public required string Code { get; set; }
}