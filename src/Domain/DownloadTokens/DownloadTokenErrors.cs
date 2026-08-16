using SharedKernel;

namespace Domain.DownloadTokens;

public static class DownloadTokenErrors
{
    public static readonly Error TokenInvalid = Error.Failure(
        "DownloadTokens.TokenInvalid",
        "The download token is invalid, expired, or has already been used.");

    public static readonly Error TokenExpired = Error.Failure(
        "DownloadTokens.TokenExpired",
        "The download token has expired. Please generate a new download link.");

    public static readonly Error TokenAlreadyUsed = Error.Failure(
        "DownloadTokens.TokenAlreadyUsed",
        "This download link has already been used. Single-use links cannot be redeemed twice.");

    public static readonly Error IpAddressMismatch = Error.Failure(
        "DownloadTokens.IpAddressMismatch",
        "This download link was generated for a different IP address and cannot be used from your current location.");

    public static Error NotFound(Guid tokenId) => Error.NotFound(
        "DownloadTokens.NotFound",
        $"Download token '{tokenId}' was not found.");
}
