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
            // FirstOrDefault returns null both when the customer doesn't exist and when their ID
            // column is null; distinguish so the caller gets an actionable error.
            bool customerExists = await context.Users
                .AnyAsync(u => u.Id == command.CustomerId && u.IsActive, cancellationToken);

            return Result.Failure<Guid>(customerExists
                ? StatementErrors.CustomerIdNumberMissing
                : StatementErrors.CustomerNotFound);
        }

        // Idempotent replay: a redelivered upload with a key we've already stored returns the
        // original statement without storing the file again. The unique index is the hard guard
        // against a concurrent race (handled after SaveChanges below).
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            Guid existingId = await context.Statements
                .Where(s => s.IdempotencyKey == command.IdempotencyKey)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId != Guid.Empty)
            {
                return existingId;
            }
        }

        // Business rule: at most one live statement per customer per period. A correction/re-issue
        // must revoke the existing statement first. Checked here (before any file work) so the caller
        // gets a clean 409 rather than a wasted store; the partial unique index is the hard guard
        // against a concurrent race (handled after SaveChanges below). This runs *after* the
        // idempotency check so a genuine redelivery of the same statement replays instead of conflicting.
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

        // Validate PDF magic bytes (%PDF-) — extension and Content-Type alone can be faked.
        byte[] magic = new byte[5];
        int bytesRead = await command.FileContent.ReadAsync(magic.AsMemory(0, 5), cancellationToken);
        if (bytesRead < 5 || !magic.AsSpan().SequenceEqual("%PDF-"u8))
        {
            metrics.UploadRejected("invalid_content");
            return Result.Failure<Guid>(StatementErrors.InvalidFileContent);
        }

        command.FileContent.Position = 0;

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
            command.IdempotencyKey);

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

            // Idempotency-key race: the winner stored the identical delivery; return it.
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                Guid winnerId = await context.Statements
                    .Where(s => s.IdempotencyKey == command.IdempotencyKey)
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (winnerId != Guid.Empty)
                {
                    return winnerId;
                }
            }

            throw;
        }

        metrics.StatementUploaded();

        return statement.Id;
    }
}
