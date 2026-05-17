using Domain.Statements.Events;
using Domain.Users;
using SharedKernel;

namespace Domain.Statements;

public sealed class Statement : Entity
{
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
    public StatementStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? RevokedByAdminId { get; private set; }

    public User Customer { get; private set; } = null!;

    public bool IsActive => Status == StatementStatus.Active;

    public static Statement Create(
        Guid customerId,
        Guid uploadedByAdminId,
        string originalFileName,
        string storagePath,
        string contentType,
        long fileSizeBytes,
        string period,
        string description)
    {
        var statement = new Statement
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            UploadedByAdminId = uploadedByAdminId,
            OriginalFileName = originalFileName,
            StoragePath = storagePath,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            Period = period,
            Description = description,
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
