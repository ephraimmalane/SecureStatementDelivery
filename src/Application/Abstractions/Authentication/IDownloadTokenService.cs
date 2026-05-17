namespace Application.Abstractions.Authentication;

public sealed record DownloadTokenClaims(
    Guid TokenId,
    Guid StatementId,
    Guid UserId,
    DateTime ExpiresAt);

public interface IDownloadTokenService
{
    (string Token, Guid TokenId) GenerateToken(Guid statementId, Guid userId, DateTime expiresAt);

    DownloadTokenClaims? ValidateToken(string token);

    string HashToken(string rawToken);
}
