using System.Text;
using Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Infrastructure.UnitTests.Security;

public class AesFieldEncryptorTests
{
    // Base64 of 32 bytes — a valid 256-bit key for tests.
    private const string Key = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static AesFieldEncryptor Create() =>
        new(Options.Create(new FieldEncryptionOptions { Key = Key }));

    [Fact]
    public void Encrypt_Then_Decrypt_Should_RoundTrip()
    {
        AesFieldEncryptor encryptor = Create();

        string cipher = encryptor.Encrypt("9001015800086");

        encryptor.Decrypt(cipher).ShouldBe("9001015800086");
    }

    [Fact]
    public void Encrypt_Should_ProduceDifferentCiphertext_ForSamePlaintext()
    {
        AesFieldEncryptor encryptor = Create();

        // A fresh random nonce per call means ciphertexts differ even for identical input.
        encryptor.Encrypt("9001015800086").ShouldNotBe(encryptor.Encrypt("9001015800086"));
    }

    [Fact]
    public void Decrypt_Should_Throw_When_CiphertextTampered()
    {
        AesFieldEncryptor encryptor = Create();
        byte[] raw = Convert.FromBase64String(encryptor.Encrypt("9001015800086"));
        raw[^1] ^= 0xFF; // flip a bit in the ciphertext body

        Should.Throw<Exception>(() => encryptor.Decrypt(Convert.ToBase64String(raw)));
    }

    [Fact]
    public void Constructor_Should_Reject_WrongLengthKey()
    {
        string shortKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("too-short"));

        Should.Throw<InvalidOperationException>(() =>
            new AesFieldEncryptor(Options.Create(new FieldEncryptionOptions { Key = shortKey })));
    }
}
