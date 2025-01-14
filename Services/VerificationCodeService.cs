using Microsoft.Extensions.Caching.Distributed;

public interface IVerificationCodeService
{
    Task<string> GenerateCodeAsync(string email);
    Task<bool> ValidateCodeAsync(string email, string code);
}

public class VerificationCodeService : IVerificationCodeService
{
    private readonly IDistributedCache _cache;
    private readonly Random _random;
    private readonly ILogger<VerificationCodeService> _logger;
    private readonly IEmailService _emailService;
    public VerificationCodeService(IDistributedCache cache, 
    IEmailService emailService,
    ILogger<VerificationCodeService> logger )
    {
        _cache = cache;
        _random = new Random();
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<string> GenerateCodeAsync(string email)
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

        await _emailService.SendEmailAsync(email, "Xians.ai - Verification Code", GetEmailBody(code), false);

        _logger.LogInformation($"Verification code generated for {email}: {code}");
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

    public async Task<bool> ValidateCodeAsync(string email, string code)
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
        }

        _logger.LogInformation($"Verification code validated for {email}: {isValid}");
        
        return isValid;
    }
}