using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.Statements;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Statements.Upload;

internal sealed class UploadStatementCommandHandler(
    IApplicationDbContext context,
    IFileStorageService fileStorage,
    IUserContext userContext) : ICommandHandler<UploadStatementCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadStatementCommand command, CancellationToken cancellationToken)
    {
        bool customerExists = await context.Users
            .AnyAsync(u => u.Id == command.CustomerId && u.IsActive, cancellationToken);

        if (!customerExists)
        {
            return Result.Failure<Guid>(StatementErrors.CustomerNotFound);
        }

        Guid adminId = userContext.UserId;
        string directory = $"statements/{command.CustomerId}";

        StoredFile storedFile = await fileStorage.StoreAsync(
            command.OriginalFileName,
            command.FileContent,
            command.ContentType,
            directory,
            cancellationToken);

        var statement = Statement.Create(
            command.CustomerId,
            adminId,
            command.OriginalFileName,
            storedFile.StoragePath,
            command.ContentType,
            storedFile.FileSizeBytes,
            command.Period,
            command.Description);

        context.Statements.Add(statement);

        context.DownloadAuditLogs.Add(DownloadAuditLog.Create(
            statement.Id,
            adminId,
            AuditAction.StatementUploaded));

        await context.SaveChangesAsync(cancellationToken);

        return statement.Id;
    }
}
