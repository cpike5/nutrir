namespace Nutrir.Infrastructure.Configuration;

public class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>
    /// Base64-encoded 256-bit encryption key.
    /// In production, set via environment variable NUTRIR_ENCRYPTION_KEY.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Current key version. Increment when rotating keys.
    /// </summary>
    public int KeyVersion { get; set; } = 1;

    /// <summary>
    /// Previous keys for decryption during rotation, keyed by version number.
    /// </summary>
    public Dictionary<int, string> PreviousKeys { get; set; } = [];

    /// <summary>
    /// Whether field encryption is enabled. Set to false to disable
    /// (e.g., during initial setup before a key is configured).
    /// </summary>
    public bool Enabled { get; set; } = true;
}
