using Application.Abstractions.Authentication;
using Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Infrastructure.UnitTests.Authentication;

// Proves the signing-key rotation overlap: a download link signed with the previous key keeps
// validating while that key remains in PreviousSecrets, and stops the moment it is dropped.
public class DownloadTokenServiceRotationTests
{
    private const string OldSecret = "old-download-token-secret-000000000000";
    private const string NewSecret = "new-download-token-secret-111111111111";

    private static DownloadTokenService Service(string secret, params string[] previousSecrets) =>
        new(Options.Create(new DownloadTokenOptions
        {
            Secret = secret,
            PreviousSecrets = previousSecrets,
            Issuer = "statement-download",
            Audience = "statement-download-clients"
        }));

    private static string TokenSignedWith(string secret, out Guid statementId, out Guid userId)
    {
        statementId = Guid.NewGuid();
        userId = Guid.NewGuid();
        (string token, _) = Service(secret).GenerateToken(
            statementId, userId, DateTime.UtcNow.AddMinutes(5));
        return token;
    }

    [Fact]
    public void ValidateToken_Should_Accept_TokenSignedWithCurrentKey()
    {
        string token = TokenSignedWith(NewSecret, out Guid statementId, out Guid userId);

        DownloadTokenClaims? claims = Service(NewSecret).ValidateToken(token);

        claims.ShouldNotBeNull();
        claims.StatementId.ShouldBe(statementId);
        claims.UserId.ShouldBe(userId);
    }

    [Fact]
    public void ValidateToken_Should_Accept_PreviousKey_DuringOverlapWindow()
    {
        // Link was signed before rotation, with the old key...
        string token = TokenSignedWith(OldSecret, out Guid statementId, out _);

        // ...and the service now signs with the new key but still accepts the old one.
        DownloadTokenClaims? claims = Service(NewSecret, OldSecret).ValidateToken(token);

        claims.ShouldNotBeNull();
        claims.StatementId.ShouldBe(statementId);
    }

    [Fact]
    public void ValidateToken_Should_Reject_PreviousKey_AfterOverlapWindowClosed()
    {
        string token = TokenSignedWith(OldSecret, out _, out _);

        // Old key dropped from PreviousSecrets — the link must no longer validate.
        DownloadTokenClaims? claims = Service(NewSecret).ValidateToken(token);

        claims.ShouldBeNull();
    }

    [Fact]
    public void GenerateToken_Should_Always_SignWithCurrentKey_NotPreviousKeys()
    {
        // A token freshly issued by the rotated service must NOT validate under the old key alone.
        (string token, _) = Service(NewSecret, OldSecret).GenerateToken(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddMinutes(5));

        Service(OldSecret).ValidateToken(token).ShouldBeNull();
        Service(NewSecret).ValidateToken(token).ShouldNotBeNull();
    }
}
