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
        // Load the customer's SA ID number (decrypted by the EF value converter): it is the
        // mandatory open password for the statement PDF.
        string? idNumber = await context.Users
            .Where(u => u.Id == command.CustomerId && u.IsActive)
            .Select(u => u.SouthAfricanIdNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (idNumber is null)
        {
            // SA ID is a required, non-null column, so a null projection means there is no active
            // customer row for this id.
            return Result.Failure<Guid>(StatementErrors.CustomerNotFound);
        }

        // Idempotent replay: a redelivered upload with a DocumentId we've already stored for this
        // customer returns the original statement without storing the file again. The per-customer
        // unique index is the hard guard against a concurrent race (handled after SaveChanges below).
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

        // Reject a file whose bytes don't match the declared content type's signature — extension and
        // Content-Type alone can be faked. The validator reads the header and rewinds the stream.
        if (!await fileTypeValidator.IsValidAsync(command.ContentType, command.FileContent, cancellationToken))
        {
            metrics.UploadRejected("invalid_content");
            return Result.Failure<Guid>(StatementErrors.InvalidFileContent);
        }

        // Cross-channel idempotency: a content fingerprint of the plaintext bytes is channel-, name-,
        // and source-independent, so the SAME file re-delivered for the SAME period via any path
        // (different DocumentId or file name, or none) resolves to the original instead of a duplicate.
        // Scoped to the period on purpose: two legitimately-different statements can be byte-identical
        // across periods (e.g. no-activity months), and those must NOT be merged. Runs before the
        // per-period conflict check so a true redelivery replays instead of returning a 409. The
        // per-(customer, period) unique index is the hard guard against a concurrent race (below).
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

        // Business rule: at most one live statement per customer per period. A correction/re-issue must
        // revoke the existing statement first. The partial unique index is the hard guard against a
        // concurrent race (handled after SaveChanges below).
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

        // Anti-malware scan before the bytes are ever promoted to permanent storage.
        if (!await contentScanner.IsCleanAsync(command.FileContent, cancellationToken))
        {
            metrics.UploadRejected("malware");
            return Result.Failure<Guid>(StatementErrors.MalwareDetected);
        }

        command.FileContent.Position = 0;

        // Every statement is AES-encrypted with the customer's SA ID number as the open password.
        // ProtectAsync also opens the PDF, so a structurally broken file is rejected here.
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

        // Canonical display name derived from trusted data (the validated YYYY-MM period), not the
        // caller-supplied file name. This makes every statement's name identical across the manual,
        // M2M push, and M2M pull paths — the sender's original name is intentionally discarded.
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
            // Period already validated upstream; if the domain invariant still rejects it,
            // don't leave the just-stored file orphaned.
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
            // A concurrent request won one of the uniqueness guards. Clean up our orphaned file and
            // resolve which guard fired by re-reading committed state — DB-agnostic, no dependency on
            // the provider's constraint-name in the exception.
            await fileStorage.DeleteAsync(storedFile.StoragePath, cancellationToken);

            // (CustomerId, Period) active-uniqueness: a live statement for this period already exists.
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

            // DocumentId race: the winner stored the identical delivery; return it.
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

            // ContentHash race: a concurrent upload of the identical file for this period won; return it.
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
