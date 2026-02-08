namespace Shared.Services;

/// <summary>
/// Service for encrypting and decrypting sensitive data.
/// Provides field-level encryption for storing secrets securely in the database.
/// </summary>
public interface ISecretsEncryptionService
{
    /// <summary>
    /// Encrypts a plain text string using the configured encryption algorithm.
    /// Returns base64-encoded encrypted data.
    /// </summary>
    /// <param name="plainText">The plain text to encrypt</param>
    /// <returns>Base64-encoded encrypted string, or null if input is null/empty</returns>
    string? Encrypt(string? plainText);

    /// <summary>
    /// Decrypts an encrypted string back to plain text.
    /// </summary>
    /// <param name="encryptedText">Base64-encoded encrypted string</param>
    /// <returns>Decrypted plain text, or null if input is null/empty</returns>
    string? Decrypt(string? encryptedText);

    /// <summary>
    /// Encrypts a JSON-serialized object.
    /// </summary>
    /// <typeparam name="T">Type of object to encrypt</typeparam>
    /// <param name="data">Object to encrypt</param>
    /// <returns>Base64-encoded encrypted JSON</returns>
    string? EncryptObject<T>(T? data) where T : class;

    /// <summary>
    /// Decrypts and deserializes a JSON object.
    /// </summary>
    /// <typeparam name="T">Type to deserialize to</typeparam>
    /// <param name="encryptedJson">Base64-encoded encrypted JSON</param>
    /// <returns>Decrypted and deserialized object</returns>
    T? DecryptObject<T>(string? encryptedJson) where T : class;
}
