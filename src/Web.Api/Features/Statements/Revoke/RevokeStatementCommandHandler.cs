using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.AuditLogs;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Statements.Revoke;

internal sealed class RevokeStatementCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext) : ICommandHandler<RevokeStatementCommand>
{
    public async Task<Result> Handle(RevokeStatementCommand command, CancellationToken cancellationToken)
    {
        Statement? statement = await context.Statements
            .SingleOrDefaultAsync(s => s.Id == command.StatementId, cancellationToken);

        if (statement is null)
        {
            return Result.Failure(StatementErrors.NotFound(command.StatementId));
        }

        Guid adminId = userContext.UserId;

        Result revokeResult = statement.Revoke(adminId, command.Reason);

        if (revokeResult.IsFailure)
        {
            return revokeResult;
        }

        context.DownloadAuditLogs.Add(DownloadAuditLog.Create(
            statement.Id,
            adminId,
            AuditAction.StatementRevoked,
            additionalData: command.Reason));

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
