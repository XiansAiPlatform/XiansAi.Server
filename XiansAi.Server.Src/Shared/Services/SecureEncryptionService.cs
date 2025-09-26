using System.Security.Cryptography;
using System.Text;

namespace Shared.Services;

public interface ISecureEncryptionService
{
    string Encrypt(string plaintext, string uniqueSecret);
    string Decrypt(string ciphertext, string uniqueSecret);
}

/// <summary>
/// Symmetric encryption using AES-256-GCM with an application-level secret.
/// The secret is read from configuration env var APP_SERVER_API_KEY to avoid
/// storing raw keys in code. Nonce is randomly generated per message.
/// </summary>
public class SecureEncryptionService : ISecureEncryptionService
{
    private readonly string _baseSecret;
    private readonly ILogger<SecureEncryptionService> _logger;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KdfIterations = 200_000;

    public SecureEncryptionService(ILogger<SecureEncryptionService> logger, IConfiguration configuration)
    {
        _logger = logger;
        var baseSecret = configuration["EncryptionKeys:BaseSecret"];
        if (string.IsNullOrWhiteSpace(baseSecret))
        {
            throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
        }
        _baseSecret = baseSecret;
    }

    public string Encrypt(string plaintext, string uniqueSecret)
    {
        if (plaintext == null) plaintext = string.Empty;
        if (string.IsNullOrWhiteSpace(uniqueSecret))
        {
            throw new ArgumentException("uniqueSecret must be provided", nameof(uniqueSecret));
        }

        var key = DeriveKey(_baseSecret, uniqueSecret);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plainBytes.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag, associatedData: null);
        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string ciphertext, string uniqueSecret)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return string.Empty;
        if (string.IsNullOrWhiteSpace(uniqueSecret))
        {
            throw new ArgumentException("uniqueSecret must be provided", nameof(uniqueSecret));
        }

        var key = DeriveKey(_baseSecret, uniqueSecret);
        var payload = Convert.FromBase64String(ciphertext);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[payload.Length - nonce.Length - tag.Length];
        Buffer.BlockCopy(payload, 0, nonce, 0, nonce.Length);
        Buffer.BlockCopy(payload, nonce.Length, tag, 0, tag.Length);
        Buffer.BlockCopy(payload, nonce.Length + tag.Length, cipher, 0, cipher.Length);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, associatedData: null);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey(string baseSecret, string uniqueSecret)
    {
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes(uniqueSecret));
        using var kdf = new Rfc2898DeriveBytes(baseSecret, salt, KdfIterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32);
    }
}

