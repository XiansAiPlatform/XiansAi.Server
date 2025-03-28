using System.ComponentModel.DataAnnotations;

namespace XiansAi.Server.Temporal;

public class TemporalConfig
{
    [Required]
    public string? ServiceAccountApiKey { get; set; }

    public string? FlowServerUrl { get; set; }

    private string? _flowServerNamespace;
    public string? FlowServerNamespace
    {
        get
        {
            if (!string.IsNullOrEmpty(_flowServerNamespace))
            {
                // if not starting with 'tenant-' then add it
                if (!_flowServerNamespace.StartsWith("tenant-"))
                {
                    return "tenant-" + _flowServerNamespace;
                }
                // if last charactor is '-' then remove it
                if (_flowServerNamespace.EndsWith("-"))
                {
                    return _flowServerNamespace.Substring(0, _flowServerNamespace.Length - 1);
                }
                return _flowServerNamespace.ToLower();
            }
            return null;
        }
        set
        {
            _flowServerNamespace = value;
        }
    }

    // optionally read from local file system
    public string? CertificateFilePath { get; set; }
    public string? PrivateKeyFilePath { get; set; }

    // optionally read from key vault as secrets
    public string? CertificateKeyVaultName { get; set; }
    public string? PrivateKeyKeyVaultName { get; set; }

    // Add new properties for base64 encoded values
    public string? CertificateBase64 { get; set; }
    public string? PrivateKeyBase64 { get; set; }
}