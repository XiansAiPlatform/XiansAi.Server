using System.Security.Cryptography;
using System.Text;
using Shared.Configuration;

namespace Shared.Services.Privacy;

/// <summary>
/// Replaces an identifier with a keyed hash. The same value always gives the same hash, so a later request
/// about the same person can be matched to earlier records, but the value cannot be recovered without the key.
/// </summary>
public interface IPseudonymiser
{
    string Pseudonymise(string value);
}

public sealed class HmacPseudonymiser : IPseudonymiser
{
    private readonly byte[] _key;

    public HmacPseudonymiser(PrivacyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var key = Encoding.UTF8.GetBytes(options.PseudonymKey ?? string.Empty);
        if (key.Length < PrivacyOptions.MinimumPseudonymKeyBytes)
        {
            throw new InvalidOperationException(
                $"{PrivacyOptions.SectionName}:{nameof(PrivacyOptions.PseudonymKey)} must be at least " +
                $"{PrivacyOptions.MinimumPseudonymKeyBytes} bytes.");
        }

        _key = key;
    }

    public string Pseudonymise(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalised = Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant());
        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, normalised));
    }
}
