using SharedKernel;

namespace Domain.Statements.Events;

public sealed record DownloadLinkGeneratedDomainEvent(
    Guid StatementId,
    Guid UserId,
    Guid DownloadTokenId) : IDomainEvent;
