using Application.Abstractions.Messaging;

namespace Application.Statements.GenerateDownloadLink;

public sealed record GenerateDownloadLinkCommand(
    Guid StatementId,
    bool IsSingleUse = true,
    int ExpiryMinutes = 15,
    string? RequestIpAddress = null) : ICommand<DownloadLinkResponse>;

public sealed record DownloadLinkResponse(
    string DownloadToken,
    DateTime ExpiresAt,
    bool IsSingleUse);
