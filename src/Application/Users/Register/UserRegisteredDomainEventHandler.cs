using Domain.Users;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.Users.Register;

internal sealed class UserRegisteredDomainEventHandler(ILogger<UserRegisteredDomainEventHandler> logger)
    : IDomainEventHandler<UserRegisteredDomainEvent>
{
    public Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "New customer registered. UserId: {UserId}. Outbox/email service integration point.",
            domainEvent.UserId);

        return Task.CompletedTask;
    }
}
