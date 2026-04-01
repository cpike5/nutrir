using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Security;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class AesGcmFieldEncryptorTests
{
    private readonly ILogger<AesGcmFieldEncryptor> _logger = Substitute.For<ILogger<AesGcmFieldEncryptor>>();

    private static string GenerateBase64Key()
    {
        var key = new byte[32]; // 256-bit key
        RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }

    private AesGcmFieldEncryptor CreateEncryptor(string? key = null, int keyVersion = 1, bool enabled = true, Dictionary<int, string>? previousKeys = null)
    {
        var options = Options.Create(new EncryptionOptions
        {
            Key = key ?? GenerateBase64Key(),
            KeyVersion = keyVersion,
            Enabled = enabled,
            PreviousKeys = previousKeys ?? []
        });
        return new AesGcmFieldEncryptor(options, _logger);
    }

    // ---------------------------------------------------------------------------
    // IsEnabled
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsEnabled_WhenEnabledWithKey_ReturnsTrue()
    {
        var sut = CreateEncryptor();
        sut.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WhenDisabled_ReturnsFalse()
    {
        var sut = CreateEncryptor(enabled: false);
        sut.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WhenKeyIsEmpty_ReturnsFalse()
    {
        var sut = CreateEncryptor(key: "", enabled: true);
        sut.IsEnabled.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // Encrypt / Decrypt roundtrip
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("")]
    [InlineData("Special chars: !@#$%^&*()")]
    [InlineData("Unicode: cafe\u0301 \u00e9\u00e8\u00ea")]
    public void EncryptThenDecrypt_ReturnsOriginalPlaintext(string plaintext)
    {
        var sut = CreateEncryptor();

        var encrypted = sut.Encrypt(plaintext);
        var decrypted = sut.Decrypt(encrypted);

        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesVersionPrefixedFormat()
    {
        var sut = CreateEncryptor(keyVersion: 2);

        var encrypted = sut.Encrypt("test data");

        encrypted.Should().StartWith("v2:");
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var sut = CreateEncryptor();
        var plaintext = "same input";

        var encrypted1 = sut.Encrypt(plaintext);
        var encrypted2 = sut.Encrypt(plaintext);

        encrypted1.Should().NotBe(encrypted2, "random nonce should produce different ciphertext");
    }

    // ---------------------------------------------------------------------------
    // Disabled passthrough
    // ---------------------------------------------------------------------------

    [Fact]
    public void Encrypt_WhenDisabled_ReturnsPlaintextUnchanged()
    {
        var sut = CreateEncryptor(enabled: false);

        var result = sut.Encrypt("sensitive data");

        result.Should().Be("sensitive data");
    }

    [Fact]
    public void Decrypt_WhenDisabled_ReturnsInputUnchanged()
    {
        var sut = CreateEncryptor(enabled: false);

        var result = sut.Decrypt("any text");

        result.Should().Be("any text");
    }

    // ---------------------------------------------------------------------------
    // Decrypt — plaintext passthrough (migration safety)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("plain text without prefix")]
    [InlineData("no colon here")]
    public void Decrypt_WithUnencryptedData_ReturnsAsIs(string input)
    {
        var sut = CreateEncryptor();

        var result = sut.Decrypt(input);

        result.Should().Be(input);
    }

    [Fact]
    public void Decrypt_WithInvalidVersionPrefix_ReturnsAsIs()
    {
        var sut = CreateEncryptor();

        var result = sut.Decrypt("vABC:notavalidversion");

        result.Should().Be("vABC:notavalidversion");
    }

    [Fact]
    public void Decrypt_WithTooShortData_ReturnsAsIs()
    {
        var sut = CreateEncryptor();

        // v1: followed by base64 that decodes to fewer than NonceSize + TagSize bytes
        var shortData = Convert.ToBase64String(new byte[4]);
        var result = sut.Decrypt($"v1:{shortData}");

        result.Should().Be($"v1:{shortData}");
    }

    // ---------------------------------------------------------------------------
    // Key version / rotation
    // ---------------------------------------------------------------------------

    [Fact]
    public void Decrypt_WithUnknownKeyVersion_ThrowsCryptographicException()
    {
        var sut = CreateEncryptor(keyVersion: 1);

        // Encrypt with version 1, then create a new encryptor at version 2 without previous keys
        var encrypted = sut.Encrypt("test");
        // Manually change the version prefix to simulate unknown version
        var tampered = "v99:" + encrypted[(encrypted.IndexOf(':') + 1)..];

        var act = () => sut.Decrypt(tampered);
        act.Should().Throw<CryptographicException>().WithMessage("*version 99*");
    }

    [Fact]
    public void Decrypt_WithPreviousKeyVersion_DecryptsSuccessfully()
    {
        var oldKey = GenerateBase64Key();
        var newKey = GenerateBase64Key();

        // Encrypt with old key (version 1)
        var oldEncryptor = CreateEncryptor(key: oldKey, keyVersion: 1);
        var encrypted = oldEncryptor.Encrypt("secret message");

        // Create new encryptor at version 2, with old key in PreviousKeys
        var newEncryptor = CreateEncryptor(
            key: newKey,
            keyVersion: 2,
            previousKeys: new Dictionary<int, string> { { 1, oldKey } });

        var decrypted = newEncryptor.Decrypt(encrypted);

        decrypted.Should().Be("secret message");
    }
}
