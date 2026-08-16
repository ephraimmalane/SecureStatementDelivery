using Domain.Statements.Events;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Web.Api.Features.Statements.Upload;

// Fires after any statement is stored (single-shot or resumable upload). This is the
// integration point for notifying the customer that a new statement is available.
//
// IMPORTANT delivery pattern: the notification must NOT embed the document or a direct
// download link. It should tell the customer to sign in and view the statement in the app
// (GET /statements/{id}/content). Emailing links to sensitive financial documents is a
// data-exposure risk; the link-based endpoint exists for explicit, opt-in share scenarios.
internal sealed class StatementUploadedDomainEventHandler(
    ILogger<StatementUploadedDomainEventHandler> logger)
    : IDomainEventHandler<StatementUploadedDomainEvent>
{
    public Task Handle(StatementUploadedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Statement {StatementId} available for customer {CustomerId}. " +
            "Notification integration point: send a 'new statement available — sign in to view' " +
            "message via the outbox/notification service (no document or link in the message). " +
            "Include the password hint so the customer knows how to open the PDF: \"{PasswordHint}\"",
            domainEvent.StatementId,
            domainEvent.CustomerId,
            StatementMessages.PasswordHint);

        return Task.CompletedTask;
    }
}
