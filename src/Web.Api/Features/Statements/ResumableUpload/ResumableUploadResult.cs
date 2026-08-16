namespace Web.Api.Features.Statements.ResumableUpload;

// Stored in Redis after a resumable upload finishes so the client can fetch the
// outcome (created statement id, or a validation error) regardless of which pod
// handled the final chunk.
public sealed record ResumableUploadResult(bool Success, Guid? StatementId, string? Error)
{
    public static string CacheKey(string tusFileId) => $"upload-result:{tusFileId}";
}
