namespace Infrastructure.Security;

// Reversible encryption for individual persisted fields that must be recoverable in plaintext
// (e.g. the SA ID number, which is reused as a PDF open password). Applied transparently by an
// EF Core value converter, so domain code keeps working with plaintext.
public interface IFieldEncryptor
{
    string Encrypt(string plaintext);

    string Decrypt(string ciphertext);
}
