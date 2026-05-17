using SharedKernel;

namespace Domain.DownloadTokens;

public sealed class DownloadToken
{
    private DownloadToken() { }

    public Guid Id { get; private set; }
    public Guid StatementId { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public bool IsUsed { get; private set; }
    public bool IsSingleUse { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public string? IpAddress { get; private set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsValid => !IsUsed && !IsExpired;

    public static DownloadToken Create(
        Guid id,
        Guid statementId,
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        bool isSingleUse = true,
        string? ipAddress = null) =>
        new()
        {
            Id = id,
            StatementId = statementId,
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IsSingleUse = isSingleUse,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
            IpAddress = ipAddress
        };

    public Result MarkAsUsed()
    {
        if (IsExpired)
        {
            return Result.Failure(DownloadTokenErrors.TokenExpired);
        }

        if (IsUsed)
        {
            return Result.Failure(DownloadTokenErrors.TokenAlreadyUsed);
        }

        IsUsed = true;
        UsedAt = DateTime.UtcNow;

        return Result.Success();
    }
}
