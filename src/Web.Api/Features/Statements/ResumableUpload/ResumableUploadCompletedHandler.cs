using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Cache;
using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace Web.Api.Features.Statements.ResumableUpload;

internal sealed class ResumableUploadCompletedHandler(
    IApplicationDbContext context,
    IFileStorageService fileStorage,
    IPdfProtector pdfProtector,
    IFileContentScanner contentScanner,
    IFileTypeValidator fileTypeValidator,
    IContentHasher contentHasher,
    IUserContext userContext,
    ICacheService cache,
    ILogger<ResumableUploadCompletedHandler> logger)
{
    public async Task HandleAsync(FileCompleteContext ctx)
    {
        CancellationToken ct = ctx.CancellationToken;
        ITusFile file = await ctx.GetFileAsync();
        Dictionary<string, Metadata> metadata = await file.GetMetadataAsync(ct);

        ResumableUploadResult result;
        try
        {
            result = await ProcessAsync(file, metadata, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resumable upload {FileId} failed during finalisation", file.Id);
            result = new ResumableUploadResult(false, null, "Upload processing failed.");
        }

        await cache.SetAsync(ResumableUploadResult.CacheKey(file.Id), result, TimeSpan.FromHours(1), ct);

        if (ctx.Store is ITusTerminationStore terminationStore)
        {
            await terminationStore.DeleteFileAsync(file.Id, ct);
        }
    }

    private async Task<ResumableUploadResult> ProcessAsync(
        ITusFile file,
        Dictionary<string, Metadata> metadata,
        CancellationToken ct)
    {
        if (!metadata.TryGetValue("customerId", out Metadata? customerIdMeta) ||
            !Guid.TryParse(customerIdMeta.GetString(Encoding.UTF8), out Guid customerId))
        {
            return new ResumableUploadResult(false, null, "Missing or invalid customerId metadata.");
        }

        string contentType = GetString(metadata, "contentType", "application/pdf");
        string period = GetString(metadata, "period", string.Empty);
        string description = GetString(metadata, "description", string.Empty);
        string documentId = GetString(metadata, "documentId", string.Empty);

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            Guid existingId = await context.Statements
                .Where(s => s.CustomerId == customerId && s.DocumentId == documentId)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(ct);

            if (existingId != Guid.Empty)
            {
                return new ResumableUploadResult(true, existingId, null);
            }
        }

        if (!Statement.IsValidPeriod(period))
        {
            return new ResumableUploadResult(false, null, StatementErrors.InvalidPeriodFormat.Description);
        }

        string canonicalFileName = $"Statement_{period}.pdf";

        string? idNumber = await context.Users
            .Where(u => u.Id == customerId && u.IsActive)
            .Select(u => u.SouthAfricanIdNumber)
            .FirstOrDefaultAsync(ct);

        if (idNumber is null)
        {
            return new ResumableUploadResult(false, null, StatementErrors.CustomerNotFound.Description);
        }

        await using Stream content = await file.GetContentAsync(ct);

        if (!await fileTypeValidator.IsValidAsync(contentType, content, ct))
        {
            return new ResumableUploadResult(false, null, StatementErrors.InvalidFileContent.Description);
        }

        string contentHash = await contentHasher.ComputeSha256Async(content, ct);

        Guid duplicateId = await context.Statements
            .Where(s => s.CustomerId == customerId && s.Period == period && s.ContentHash == contentHash)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (duplicateId != Guid.Empty)
        {
            return new ResumableUploadResult(true, duplicateId, null);
        }

        bool activeStatementExists = await context.Statements
            .AnyAsync(
                s => s.CustomerId == customerId
                     && s.Period == period
                     && s.Status == StatementStatus.Active,
                ct);

        if (activeStatementExists)
        {
            return new ResumableUploadResult(
                false, null, StatementErrors.ActiveStatementExistsForPeriod(period).Description);
        }

        if (!await contentScanner.IsCleanAsync(content, ct))
        {
            return new ResumableUploadResult(false, null, StatementErrors.MalwareDetected.Description);
        }

        content.Position = 0;

        Stream protectedStream;
        try
        {
            protectedStream = await pdfProtector.ProtectAsync(content, idNumber, ct);
        }
        catch
        {
            return new ResumableUploadResult(false, null, StatementErrors.InvalidFileContent.Description);
        }

        Guid adminId = userContext.UserId;

        StoredFile storedFile;
        try
        {
            storedFile = await fileStorage.StoreAsync(
                canonicalFileName,
                protectedStream,
                contentType,
                $"statements/{customerId}",
                ct);
        }
        finally
        {
            await protectedStream.DisposeAsync();
        }

        Result<Statement> statementResult = Statement.Create(
            customerId,
            adminId,
            canonicalFileName,
            storedFile.StoragePath,
            contentType,
            storedFile.FileSizeBytes,
            period,
            description,
            isPasswordProtected: true,
            documentId,
            contentHash);

        if (statementResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storedFile.StoragePath, ct);
            return new ResumableUploadResult(false, null, statementResult.Error.Description);
        }

        Statement statement = statementResult.Value;

        context.Statements.Add(statement);
        context.AuditLogs.Add(AuditLog.Create(statement.Id, adminId, AuditAction.StatementUploaded));

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await fileStorage.DeleteAsync(storedFile.StoragePath, ct);

            bool activeExists = await context.Statements
                .AnyAsync(
                    s => s.CustomerId == customerId
                         && s.Period == period
                         && s.Status == StatementStatus.Active,
                    ct);

            if (activeExists)
            {
                return new ResumableUploadResult(
                    false, null, StatementErrors.ActiveStatementExistsForPeriod(period).Description);
            }

            if (!string.IsNullOrWhiteSpace(documentId))
            {
                Guid winnerId = await context.Statements
                    .Where(s => s.CustomerId == customerId && s.DocumentId == documentId)
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync(ct);

                if (winnerId != Guid.Empty)
                {
                    return new ResumableUploadResult(true, winnerId, null);
                }
            }

            Guid contentWinnerId = await context.Statements
                .Where(s => s.CustomerId == customerId
                            && s.Period == period
                            && s.ContentHash == contentHash)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(ct);

            if (contentWinnerId != Guid.Empty)
            {
                return new ResumableUploadResult(true, contentWinnerId, null);
            }

            throw;
        }

        return new ResumableUploadResult(true, statement.Id, null);
    }

    private static string GetString(Dictionary<string, Metadata> metadata, string key, string fallback) =>
        metadata.TryGetValue(key, out Metadata? value) ? value.GetString(Encoding.UTF8) : fallback;
}
