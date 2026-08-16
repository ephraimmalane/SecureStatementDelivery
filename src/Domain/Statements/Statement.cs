using System.Text.RegularExpressions;
using Domain.Statements.Events;
using Domain.Users;
using SharedKernel;

namespace Domain.Statements;

public sealed partial class Statement : Entity
{
    // Canonical statement period: four-digit year, two-digit month 01-12 (e.g. 2024-01).
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex PeriodFormat();

    // Authoritative period invariant. Every write path (multipart and resumable upload)
    // funnels through Create, so enforcing the format here guarantees the read-side
    // equality filter in the list query can never silently miss a statement.
    public static bool IsValidPeriod(string? period) =>
        period is not null && PeriodFormat().IsMatch(period.Trim());

    private Statement() { }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid UploadedByAdminId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoragePath { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string Period { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsPasswordProtected { get; private set; }

    // Optional caller-supplied key that makes ingestion idempotent: a redelivered upload
    // (retried webhook / double-submit) carrying the same key is deduplicated rather than
    // creating a second statement. Enforced by a unique index in the persistence config.
    public string? IdempotencyKey { get; private set; }

    public StatementStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? RevokedByAdminId { get; private set; }

    public User Customer { get; private set; } = null!;

    public bool IsActive => Status == StatementStatus.Active;

    public static Result<Statement> Create(
        Guid customerId,
        Guid uploadedByAdminId,
        string originalFileName,
        string storagePath,
        string contentType,
        long fileSizeBytes,
        string period,
        string description,
        bool isPasswordProtected = false,
        string? idempotencyKey = null)
    {
        // Normalise and validate the period before the entity exists, so a malformed value
        // can never be persisted regardless of which upload path produced it.
        string normalizedPeriod = period?.Trim() ?? string.Empty;

        if (!IsValidPeriod(normalizedPeriod))
        {
            return Result.Failure<Statement>(StatementErrors.InvalidPeriodFormat);
        }

        var statement = new Statement
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            UploadedByAdminId = uploadedByAdminId,
            OriginalFileName = originalFileName,
            StoragePath = storagePath,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            Period = normalizedPeriod,
            Description = description,
            IsPasswordProtected = isPasswordProtected,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            Status = StatementStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        statement.Raise(new StatementUploadedDomainEvent(statement.Id, customerId, uploadedByAdminId));

        return statement;
    }

    public Result Revoke(Guid revokedByAdminId, string reason)
    {
        if (Status == StatementStatus.Revoked)
        {
            return Result.Failure(StatementErrors.AlreadyRevoked);
        }

        Status = StatementStatus.Revoked;
        RevokedAt = DateTime.UtcNow;
        RevokedReason = reason;
        RevokedByAdminId = revokedByAdminId;

        Raise(new StatementRevokedDomainEvent(Id, CustomerId, revokedByAdminId));

        return Result.Success();
    }
}
