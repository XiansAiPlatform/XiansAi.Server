using System.Security.Cryptography;
using System.Text;

namespace Shared.Utils;

// Content-addressing hash for change detection (e.g. knowledge base versioning).
// Uses SHA-256 — SHA-1 was deprecated in 2017 (SHAttered collision attack) and is
// unsuitable for any integrity-bearing usage. Existing SHA-1 hashes already stored
// in the database remain valid as opaque version identifiers (40-char hex) and will
// be naturally replaced with 64-char SHA-256 hex on the next content write.
public static class HashGenerator
{
    public static string GenerateContentHash(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var contentBytes = Encoding.UTF8.GetBytes(content);
        return ComputeSha256Hex(contentBytes);
    }

    public static string GenerateContentHash(byte[] content)
    {
        if (content == null || content.Length == 0)
            return string.Empty;

        return ComputeSha256Hex(content);
    }

    private static string ComputeSha256Hex(byte[] contentBytes)
    {
        var hashBytes = SHA256.HashData(contentBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
