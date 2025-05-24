namespace Features.WebApi.Models;

/// <summary>
/// Request model for registering a new tenant.
/// </summary>
public class RegisterTenantRequest
{
    /// <summary>
    /// The company email address.
    /// </summary>
    public required string CompanyEmail { get; set; }
    
    /// <summary>
    /// The company website URL.
    /// </summary>
    public required string CompanyUrl { get; set; }
    
    /// <summary>
    /// The tenant identifier.
    /// </summary>
    public required string TenantId { get; set; }
    
    /// <summary>
    /// The subscription type for the tenant.
    /// </summary>
    public required string SubscriptionType { get; set; }
}

/// <summary>
/// Request model for validating a verification code.
/// </summary>
public class ValidateCodeRequest
{
    /// <summary>
    /// The email address to validate the code for.
    /// </summary>
    public required string Email { get; set; }
    
    /// <summary>
    /// The verification code to validate.
    /// </summary>
    public required string Code { get; set; }
}

/// <summary>
/// Result model for sending verification code.
/// </summary>
public class SendVerificationCodeResult
{
    /// <summary>
    /// Success message indicating the verification code was sent.
    /// </summary>
    public required string Message { get; set; }
} 