using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.Statements;
using Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Statements.Upload;

internal sealed class UploadStatementCommandHandler(
    IApplicationDbContext context,
    IFileStorageService fileStorage,
    IPdfProtector pdfProtector,
    IFileContentScanner contentScanner,
    IFileTypeValidator fileTypeValidator,
    IContentHasher contentHasher,
    StatementMetrics metrics) : ICommandHandler<UploadStatementCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadStatementCommand command, CancellationToken cancellationToken)
    {
        string? idNumber = await context.Users
            .Where(u => u.Id == command.CustomerId && u.IsActive)
            .Select(u => u.SouthAfricanIdNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (idNumber is null)
        {
            return Result.Failure<Guid>(StatementErrors.CustomerNotFound);
        }

        if (!string.IsNullOrWhiteSpace(command.DocumentId))
        {
            Guid existingId = await context.Statements
                .Where(s => s.CustomerId == command.CustomerId && s.DocumentId == command.DocumentId)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId != Guid.Empty)
            {
                return Result.Success(existingId);
            }
        }

        if (!await fileTypeValidator.IsValidAsync(command.ContentType, command.FileContent, cancellationToken))
        {
            metrics.UploadRejected("invalid_content");
            return Result.Failure<Guid>(StatementErrors.InvalidFileContent);
        }

        string contentHash = await contentHasher.ComputeSha256Async(command.FileContent, cancellationToken);

        Guid duplicateId = await context.Statements
            .Where(s => s.CustomerId == command.CustomerId
                        && s.Period == command.Period
                        && s.ContentHash == contentHash)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (duplicateId != Guid.Empty)
        {
            return Result.Success(duplicateId);
        }

        bool activeStatementExists = await context.Statements
            .AnyAsync(
                s => s.CustomerId == command.CustomerId
                     && s.Period == command.Period
                     && s.Status == StatementStatus.Active,
                cancellationToken);

        if (activeStatementExists)
        {
            return Result.Failure<Guid>(StatementErrors.ActiveStatementExistsForPeriod(command.Period));
        }

        if (!await contentScanner.IsCleanAsync(command.FileContent, cancellationToken))
        {
            metrics.UploadRejected("malware");
            return Result.Failure<Guid>(StatementErrors.MalwareDetected);
        }

        command.FileContent.Position = 0;

        Stream protectedStream;
        try
        {
            protectedStream = await pdfProtector.ProtectAsync(
                command.FileContent,
                idNumber,
                cancellationToken);
        }
        catch
        {
            metrics.UploadRejected("invalid_content");
            return Result.Failure<Guid>(StatementErrors.InvalidFileContent);
        }

        Guid principalId = command.UploadedByPrincipalId;
        string directory = $"statements/{command.CustomerId}";

        string canonicalFileName = $"Statement_{command.Period}.pdf";

        StoredFile storedFile;
        try
        {
            storedFile = await fileStorage.StoreAsync(
                canonicalFileName,
                protectedStream,
                command.ContentType,
                directory,
                cancellationToken);
        }
        finally
        {
            await protectedStream.DisposeAsync();
        }

        Result<Statement> statementResult = Statement.Create(
            command.CustomerId,
            principalId,
            canonicalFileName,
            storedFile.StoragePath,
            command.ContentType,
            storedFile.FileSizeBytes,
            command.Period,
            command.Description,
            isPasswordProtected: true,
            command.DocumentId,
            contentHash);

        if (statementResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storedFile.StoragePath, cancellationToken);
            return Result.Failure<Guid>(statementResult.Error);
        }

        Statement statement = statementResult.Value;

        context.Statements.Add(statement);

        context.DownloadAuditLogs.Add(DownloadAuditLog.Create(
            statement.Id,
            principalId,
            AuditAction.StatementUploaded));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await fileStorage.DeleteAsync(storedFile.StoragePath, cancellationToken);

            bool activeExists = await context.Statements
                .AnyAsync(
                    s => s.CustomerId == command.CustomerId
                         && s.Period == command.Period
                         && s.Status == StatementStatus.Active,
                    cancellationToken);

            if (activeExists)
            {
                return Result.Failure<Guid>(StatementErrors.ActiveStatementExistsForPeriod(command.Period));
            }

            if (!string.IsNullOrWhiteSpace(command.DocumentId))
            {
                Guid winnerId = await context.Statements
                    .Where(s => s.CustomerId == command.CustomerId && s.DocumentId == command.DocumentId)
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (winnerId != Guid.Empty)
                {
                    return Result.Success(winnerId);
                }
            }

            Guid contentWinnerId = await context.Statements
                .Where(s => s.CustomerId == command.CustomerId
                            && s.Period == command.Period
                            && s.ContentHash == contentHash)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (contentWinnerId != Guid.Empty)
            {
                return Result.Success(contentWinnerId);
            }

            throw;
        }

        metrics.StatementUploaded();

        return Result.Success(statement.Id);
    }
}
