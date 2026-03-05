using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shared.Services;

/// <summary>
/// AES-256 encryption implementation for securing sensitive data.
/// Uses encryption keys from configuration with support for key rotation.
/// </summary>
public class SecretsEncryptionService : ISecretsEncryptionService
{
    private readonly ILogger<SecretsEncryptionService> _logger;
    private readonly byte[] _encryptionKey;
    private readonly string _keyId;

    public SecretsEncryptionService(
        IConfiguration configuration,
        ILogger<SecretsEncryptionService> logger)
    {
        _logger = logger;

        // Get encryption key from configuration
        var keyBase64 = configuration["EncryptionKeys:UniqueSecrets:AppIntegrationSecretKey"]
            ?? throw new InvalidOperationException(
                "EncryptionKeys:UniqueSecrets:AppIntegrationSecretKey not found in configuration. " +
                "Generate a key using: openssl rand -base64 32");

        var rawLength = keyBase64.Length;
        var hadLeadingWhitespace = keyBase64.Length > 0 && char.IsWhiteSpace(keyBase64[0]);
        var hadTrailingWhitespace = keyBase64.Length > 0 && char.IsWhiteSpace(keyBase64[^1]);
        var hadNewlines = keyBase64.Contains('\n') || keyBase64.Contains('\r');

        // Trim whitespace/newlines that often get introduced when storing in env vars (e.g. Azure)
        keyBase64 = keyBase64.Trim().Replace("\r", "").Replace("\n", "");
        var trimmedLength = keyBase64.Length;

        if (rawLength != trimmedLength || hadLeadingWhitespace || hadTrailingWhitespace || hadNewlines)
        {
            _logger.LogWarning(
                "Encryption key had extra whitespace: rawLength={RawLength}, trimmedLength={TrimmedLength}, " +
                "hadLeadingWhitespace={HadLeading}, hadTrailingWhitespace={HadTrailing}, hadNewlines={HadNewlines}. " +
                "Consider fixing the configuration to avoid trim-related issues.",
                rawLength, trimmedLength, hadLeadingWhitespace, hadTrailingWhitespace, hadNewlines);
        }

        _keyId = configuration["Encryption:KeyId"] ?? "default";

        try
        {
            _encryptionKey = Convert.FromBase64String(keyBase64);

            if (_encryptionKey.Length != 32)
            {
                throw new InvalidOperationException(
                    $"Encryption key must be 32 bytes (256 bits). Current length: {_encryptionKey.Length}. " +
                    "Generate a valid key using: openssl rand -base64 32");
            }

            _logger.LogInformation("Secrets encryption service initialized with key ID: {KeyId}", _keyId);
        }
        catch (FormatException ex)
        {
            var invalid = GetInvalidBase64CharInfo(keyBase64);
            _logger.LogError(ex,
                "Invalid Base-64 format for EncryptionKeys:UniqueSecrets:AppIntegrationSecretKey. " +
                "Length={Length}, ExpectedLength=44, InvalidChars={InvalidInfo}. " +
                "Ensure the value contains only valid Base-64 characters (A-Z, a-z, 0-9, +, /, =) with no spaces, newlines, or quotes. " +
                "Generate a key using: openssl rand -base64 32",
                keyBase64.Length,
                invalid);
            throw new InvalidOperationException(
                "Encryption key must be valid Base-64 (44 characters from: openssl rand -base64 32). " +
                "Check for hidden whitespace or invalid characters in your configuration.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize encryption service");
            throw;
        }
    }

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        try
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var msEncrypt = new MemoryStream();
            
            // Write key ID and IV to the stream (needed for decryption)
            using (var writer = new BinaryWriter(msEncrypt, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(_keyId);
                writer.Write(aes.IV.Length);
                writer.Write(aes.IV);
            }

            // Encrypt the data
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plainText);
            }

            return Convert.ToBase64String(msEncrypt.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data");
            throw new InvalidOperationException("Failed to encrypt data", ex);
        }
    }

    public string? Decrypt(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return encryptedText;

        try
        {
            var cipherBytes = Convert.FromBase64String(encryptedText);

            using var msDecrypt = new MemoryStream(cipherBytes);
            
            // Read key ID and IV
            string keyId;
            byte[] iv;
            using (var reader = new BinaryReader(msDecrypt, Encoding.UTF8, leaveOpen: true))
            {
                keyId = reader.ReadString();
                var ivLength = reader.ReadInt32();
                iv = reader.ReadBytes(ivLength);
            }

            // Verify key ID matches (for future key rotation support)
            if (keyId != _keyId)
            {
                _logger.LogWarning(
                    "Encrypted data uses different key ID: {EncryptedKeyId}, current: {CurrentKeyId}. " +
                    "Key rotation may be needed.", keyId, _keyId);
                // For now, continue with current key - in future, support multiple keys
            }

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);
            
            return srDecrypt.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data");
            throw new InvalidOperationException("Failed to decrypt data", ex);
        }
    }

    public string? EncryptObject<T>(T? data) where T : class
    {
        if (data == null)
            return null;

        var json = JsonSerializer.Serialize(data);
        return Encrypt(json);
    }

    public T? DecryptObject<T>(string? encryptedJson) where T : class
    {
        if (string.IsNullOrEmpty(encryptedJson))
            return null;

        var json = Decrypt(encryptedJson);
        if (string.IsNullOrEmpty(json))
            return null;

        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// Returns diagnostic info for characters that are not valid Base-64 (position and char code).
    /// Used for logging only; does not expose secret content. Char codes help identify BOM (65279), smart quotes, etc.
    /// </summary>
    private static string GetInvalidBase64CharInfo(string s)
    {
        var parts = new List<string>();
        var count = 0;
        for (var i = 0; i < s.Length && count < 10; i++)
        {
            var c = s[i];
            var isBase64 = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '+' || c == '/' || c == '=';
            if (!isBase64)
            {
                parts.Add($"pos{i}:code{(int)c}");
                count++;
            }
        }
        return parts.Count > 0 ? string.Join(";", parts) : "none";
    }
}
