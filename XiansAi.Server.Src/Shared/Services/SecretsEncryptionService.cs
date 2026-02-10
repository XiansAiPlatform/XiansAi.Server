using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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

        _keyId = configuration["Encryption:KeyId"] ?? "default";

        try
        {
            _encryptionKey = Convert.FromBase64String(keyBase64);
            
            if (_encryptionKey.Length != 32)
            {
                throw new InvalidOperationException(
                    $"Encryption key must be 32 bytes (256 bits). Current length: {_encryptionKey.Length}");
            }

            _logger.LogInformation("Secrets encryption service initialized with key ID: {KeyId}", _keyId);
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
}
