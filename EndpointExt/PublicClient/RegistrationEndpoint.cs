

using XiansAi.Server.Auth;
using XiansAi.Server.Temporal;

public class RegistrationEndpoint 
{
    private readonly IAuth0MgtAPIConnect _auth0MgtAPIConnect;
    private readonly ILogger<RegistrationEndpoint> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly ITemporalClientService _temporalClientService;
    private readonly IVerificationCodeService _verificationCodeService;
    public RegistrationEndpoint(IAuth0MgtAPIConnect auth0MgtAPIConnect, ITemporalClientService temporalClientService,
    IVerificationCodeService verificationCodeService,
    ILogger<RegistrationEndpoint> logger,
    ITenantContext tenantContext)
    {
        _auth0MgtAPIConnect = auth0MgtAPIConnect;
        _logger = logger;
        _tenantContext = tenantContext;
        _temporalClientService = temporalClientService;
        _verificationCodeService = verificationCodeService;
    }

    public async Task<bool> ValidateCode(string email, string code)
    {
        var isValid = await _verificationCodeService.ValidateCodeAsync(email, code);
        return isValid;
    }

    public async Task<IResult> SendVerificationCode(string email)
    {
        var code = await _verificationCodeService.GenerateCodeAsync(email);
        return Results.Ok($"Verification code sent to {email}");
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