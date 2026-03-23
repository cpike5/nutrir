using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Infrastructure.Configuration;

namespace Nutrir.Infrastructure.Security;

public class AesGcmFieldEncryptor
{
    private const int NonceSize = 12; // 96-bit nonce (standard for GCM)
    private const int TagSize = 16;   // 128-bit authentication tag

    // Static instance set during DI registration, used by EF Core value converters
    // in pooled DbContext where constructor injection isn't available.
    internal static AesGcmFieldEncryptor? Instance { get; set; }

    private readonly EncryptionOptions _options;
    private readonly ILogger<AesGcmFieldEncryptor> _logger;

    public AesGcmFieldEncryptor(
        IOptions<EncryptionOptions> options,
        ILogger<AesGcmFieldEncryptor> logger)
    {
        _options = options.Value;
        Instance = this;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled && !string.IsNullOrEmpty(_options.Key);

    public string Encrypt(string plaintext)
    {
        if (!IsEnabled) return plaintext;

        var key = Convert.FromBase64String(_options.Key);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: v{version}:{base64(nonce + ciphertext + tag)}
        var combined = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceSize + ciphertext.Length, TagSize);

        return $"v{_options.KeyVersion}:{Convert.ToBase64String(combined)}";
    }

    public string Decrypt(string encryptedText)
    {
        if (!IsEnabled) return encryptedText;

        // Plaintext passthrough — if not encrypted, return as-is (migration safety)
        if (!encryptedText.StartsWith("v", StringComparison.Ordinal) ||
            !encryptedText.Contains(':'))
        {
            return encryptedText;
        }

        var colonIndex = encryptedText.IndexOf(':');
        var versionStr = encryptedText[1..colonIndex];
        if (!int.TryParse(versionStr, out var keyVersion))
        {
            return encryptedText; // Not encrypted data
        }

        var key = GetKeyForVersion(keyVersion);
        if (key is null)
        {
            _logger.LogError("No encryption key found for version {KeyVersion}", keyVersion);
            throw new CryptographicException($"No encryption key found for version {keyVersion}");
        }

        var combined = Convert.FromBase64String(encryptedText[(colonIndex + 1)..]);
        if (combined.Length < NonceSize + TagSize)
        {
            return encryptedText; // Too short to be encrypted data
        }

        var nonce = combined[..NonceSize];
        var ciphertextLength = combined.Length - NonceSize - TagSize;
        var ciphertext = combined[NonceSize..(NonceSize + ciphertextLength)];
        var tag = combined[(NonceSize + ciphertextLength)..];

        var plaintext = new byte[ciphertextLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    private byte[]? GetKeyForVersion(int version)
    {
        if (version == _options.KeyVersion && !string.IsNullOrEmpty(_options.Key))
        {
            return Convert.FromBase64String(_options.Key);
        }

        if (_options.PreviousKeys.TryGetValue(version, out var previousKey))
        {
            return Convert.FromBase64String(previousKey);
        }

        return null;
    }
}
