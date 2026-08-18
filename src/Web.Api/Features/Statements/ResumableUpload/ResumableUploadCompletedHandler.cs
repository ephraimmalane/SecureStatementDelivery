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

// Scoped per final-chunk request. Runs when a TUS upload reaches its declared length:
// validates the assembled file, promotes it into permanent storage, creates the Statement,
// publishes the result to Redis, and removes the temporary TUS file.
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

        // Publish the outcome so the client can poll for it cross-pod.
        await cache.SetAsync(ResumableUploadResult.CacheKey(file.Id), result, TimeSpan.FromHours(1), ct);

        // Remove the temporary chunked file regardless of outcome.
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

        // Idempotent replay: a re-finalised upload carrying a DocumentId we've already stored for this
        // customer returns the original statement instead of creating a duplicate.
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

        // Reject a malformed period before storing anything, so the resumable path can't
        // persist a value the list query's exact-match filter would never find.
        if (!Statement.IsValidPeriod(period))
        {
            return new ResumableUploadResult(false, null, StatementErrors.InvalidPeriodFormat.Description);
        }

        // Canonical display name derived from the validated period — the client's file name is
        // intentionally discarded, matching the multipart and M2M paths so every statement is named
        // Statement_{YYYY-MM}.pdf regardless of upload channel.
        string canonicalFileName = $"Statement_{period}.pdf";

        // The customer's SA ID number (decrypted by the value converter) is the mandatory open
        // password for the statement PDF.
        string? idNumber = await context.Users
            .Where(u => u.Id == customerId && u.IsActive)
            .Select(u => u.SouthAfricanIdNumber)
            .FirstOrDefaultAsync(ct);

        if (idNumber is null)
        {
            // SA ID is a required, non-null column, so a null projection means the customer row
            // does not exist.
            return new ResumableUploadResult(false, null, StatementErrors.CustomerNotFound.Description);
        }

        await using Stream content = await file.GetContentAsync(ct);

        // Reject a file whose bytes don't match the declared content type's signature — extension and
        // Content-Type alone can be faked. The validator reads the header and rewinds the stream.
        if (!await fileTypeValidator.IsValidAsync(contentType, content, ct))
        {
            return new ResumableUploadResult(false, null, StatementErrors.InvalidFileContent.Description);
        }

        // Cross-channel idempotency: the same file (identical bytes) already stored for this customer
        // and period resolves to the original instead of creating a duplicate. Scoped to the period so
        // byte-identical statements for different periods are never merged. The hasher rewinds the stream.
        string contentHash = await contentHasher.ComputeSha256Async(content, ct);

        Guid duplicateId = await context.Statements
            .Where(s => s.CustomerId == customerId && s.Period == period && s.ContentHash == contentHash)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (duplicateId != Guid.Empty)
        {
            return new ResumableUploadResult(true, duplicateId, null);
        }

        // Anti-malware scan before promoting the assembled file into permanent storage.
        if (!await contentScanner.IsCleanAsync(content, ct))
        {
            return new ResumableUploadResult(false, null, StatementErrors.MalwareDetected.Description);
        }

        content.Position = 0;

        // Every statement is AES-encrypted with the customer's SA ID number as the open password.
        // ProtectAsync also opens the PDF, so a structurally broken file is rejected here.
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
        context.DownloadAuditLogs.Add(DownloadAuditLog.Create(statement.Id, adminId, AuditAction.StatementUploaded));
        await context.SaveChangesAsync(ct);

        return new ResumableUploadResult(true, statement.Id, null);
    }

    private static string GetString(Dictionary<string, Metadata> metadata, string key, string fallback) =>
        metadata.TryGetValue(key, out Metadata? value) ? value.GetString(Encoding.UTF8) : fallback;
}
