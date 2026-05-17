using SharedKernel;

namespace Domain.Statements.Events;

public sealed record StatementRevokedDomainEvent(
    Guid StatementId,
    Guid CustomerId,
    Guid RevokedByAdminId) : IDomainEvent;
