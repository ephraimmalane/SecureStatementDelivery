using SharedKernel;

namespace Domain.Statements.Events;

public sealed record StatementUploadedDomainEvent(
    Guid StatementId,
    Guid CustomerId,
    Guid UploadedByAdminId) : IDomainEvent;
