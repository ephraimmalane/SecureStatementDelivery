namespace Web.Api.Features.Statements.ResumableUpload;

public sealed record ResumableUploadResult(bool Success, Guid? StatementId, string? Error)
{
    public static string CacheKey(string tusFileId) => $"upload-result:{tusFileId}";
}
