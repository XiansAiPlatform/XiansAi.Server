using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Tests.TestUtils;

/// <summary>
/// Builds the PFX-shaped API key Xians.Lib expects (base64-encoded X.509 with tenant/user in the subject).
/// </summary>
public static class XiansLibTestCertificate
{
    public static string CreateApiKey(string tenantId, string userId)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={userId}, OU={userId}, O={tenantId}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, string.Empty));
    }
}
