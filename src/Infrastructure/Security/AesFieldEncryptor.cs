using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security;

// AES-256-GCM authenticated encryption. Output layout (then base64): nonce(12) || tag(16) || ciphertext.
// GCM gives both confidentiality and integrity, so a tampered ciphertext fails to decrypt rather
// than yielding garbage. A fresh random nonce per call means identical plaintexts differ on disk.
internal sealed class AesFieldEncryptor : IFieldEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesFieldEncryptor(IOptions<FieldEncryptionOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.Key);

        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                "FieldEncryption:Key must be a base64-encoded 256-bit (32-byte) key.");
        }
    }

    public string Encrypt(string plaintext)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipherBytes = new byte[plainBytes.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        byte[] output = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, output, NonceSize + TagSize, cipherBytes.Length);

        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        byte[] data = Convert.FromBase64String(ciphertext);

        byte[] nonce = data[..NonceSize];
        byte[] tag = data[NonceSize..(NonceSize + TagSize)];
        byte[] cipherBytes = data[(NonceSize + TagSize)..];
        byte[] plainBytes = new byte[cipherBytes.Length];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }
}
