using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Users.SetIdNumber;

internal sealed class SetCustomerIdNumberCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SetCustomerIdNumberCommand>
{
    public async Task<Result> Handle(SetCustomerIdNumberCommand command, CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == command.CustomerId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.CustomerId));
        }

        // The domain method re-validates, so an invalid value can never be persisted even if the
        // command bypassed FluentValidation. The EF value converter encrypts it on save.
        Result result = user.SetSouthAfricanIdNumber(command.SouthAfricanIdNumber);
        if (result.IsFailure)
        {
            return result;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
