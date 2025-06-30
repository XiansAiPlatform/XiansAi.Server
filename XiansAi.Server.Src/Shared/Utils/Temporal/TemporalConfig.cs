using System.ComponentModel.DataAnnotations;

namespace Shared.Utils.Temporal;

public class TemporalConfig
{
    public string? FlowServerUrl { get; set; }

    public string? FlowServerUrlExternal { get; set; }

    public string? FlowServerNamespace { get; set; }

    // optionally read from local file system
    public string? CertificateBase64 { get; set; }
    public string? PrivateKeyBase64 { get; set; }
}