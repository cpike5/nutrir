using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Nutrir.Infrastructure.Security;

public class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter(AesGcmFieldEncryptor encryptor)
        : base(
            v => v == null ? null : encryptor.Encrypt(v),
            v => v == null ? null : encryptor.Decrypt(v))
    {
    }
}
