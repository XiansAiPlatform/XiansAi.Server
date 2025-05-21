using Microsoft.AspNetCore.Authentication;

namespace Features.AgentApi.Auth;

public class CertificateAuthenticationOptions : AuthenticationSchemeOptions
{
    public static string DefaultScheme => "Certificate";
} 