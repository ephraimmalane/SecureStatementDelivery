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

        string filename = GetString(metadata, "filename", "statement.pdf");
        string contentType = GetString(metadata, "contentType", "application/pdf");
        string period = GetString(metadata, "period", string.Empty);
        string description = GetString(metadata, "description", string.Empty);
        string idempotencyKey = GetString(metadata, "idempotencyKey", string.Empty);

        // Idempotent replay: a re-finalised upload carrying a key we've already stored returns
        // the original statement instead of creating a duplicate.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            Guid existingId = await context.Statements
                .Where(s => s.IdempotencyKey == idempotencyKey)
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

        // The customer's SA ID number (decrypted by the value converter) is the mandatory open
        // password for the statement PDF.
        string? idNumber = await context.Users
            .Where(u => u.Id == customerId && u.IsActive)
            .Select(u => u.SouthAfricanIdNumber)
            .FirstOrDefaultAsync(ct);

        if (idNumber is null)
        {
            bool customerExists = await context.Users
                .AnyAsync(u => u.Id == customerId && u.IsActive, ct);

            return new ResumableUploadResult(false, null, customerExists
                ? StatementErrors.CustomerIdNumberMissing.Description
                : StatementErrors.CustomerNotFound.Description);
        }

        await using Stream content = await file.GetContentAsync(ct);

        // Validate PDF magic bytes (%PDF-) — extension and Content-Type alone can be faked.
        byte[] magic = new byte[5];
        int bytesRead = await content.ReadAsync(magic.AsMemory(0, 5), ct);
        if (bytesRead < 5 || !magic.AsSpan().SequenceEqual("%PDF-"u8))
        {
            return new ResumableUploadResult(false, null, StatementErrors.InvalidFileContent.Description);
        }

        content.Position = 0;

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
                filename,
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
            filename,
            storedFile.StoragePath,
            contentType,
            storedFile.FileSizeBytes,
            period,
            description,
            isPasswordProtected: true,
            idempotencyKey);

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
