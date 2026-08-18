using System.Net;
using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IntegrationTests;

// End-to-end proof of the download-link security model over the real HTTP pipeline: a signed link
// is only redeemable while it is unexpired, from the IP it was bound to, for a statement that is
// still active — and every attempt, allowed or denied, is written to the audit trail. Complements
// SingleUseDownloadTokenTests (which proves the single-use guarantee) by covering the remaining
// guards. All state is seeded directly so each guard can be isolated.
public sealed class DownloadTokenSecurityTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ExpiredToken_Should_BeRejected_AndTokenNotConsumed()
    {
        HttpClient client = _factory.CreateClient();
        SeededToken seeded = await SeedAsync(expiresAt: DateTime.UtcNow.AddMinutes(-1));

        HttpResponseMessage response = await client.GetAsync(DownloadUrl(seeded.Token));

        // An expired link is a client error, and the single-use flag must remain unset so a clock
        // fix or key confusion can never turn an expired token into a spent-but-valid one.
        ((int)response.StatusCode).ShouldBeInRange(400, 499);
        (await TokenIsUsedAsync(seeded.TokenId)).ShouldBeFalse();
        (await HasAuditAsync(seeded.StatementId, AuditAction.StatementDownloaded)).ShouldBeFalse();
    }

    [Fact]
    public async Task RevokedStatement_Should_NotBeDownloadable_AndTokenNotConsumed()
    {
        HttpClient client = _factory.CreateClient();
        SeededToken seeded = await SeedAsync(revoked: true);

        HttpResponseMessage response = await client.GetAsync(DownloadUrl(seeded.Token));

        // Revocation is a logical status change (WORM-compatible): the token still validates and the
        // file still exists, but no download path serves a revoked statement.
        ((int)response.StatusCode).ShouldBeInRange(400, 499);
        (await TokenIsUsedAsync(seeded.TokenId)).ShouldBeFalse();
    }

    [Fact]
    public async Task IpBoundToken_Should_BeRejected_FromDifferentIp_AndAuditDenied()
    {
        HttpClient client = _factory.CreateClient();
        SeededToken seeded = await SeedAsync(ipAddress: "198.51.100.7");

        using var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl(seeded.Token));
        request.Headers.Add(StatementDeliveryWebApplicationFactory.TestClientIpHeader, "203.0.113.9");
        HttpResponseMessage response = await client.SendAsync(request);

        ((int)response.StatusCode).ShouldBeInRange(400, 499);
        (await TokenIsUsedAsync(seeded.TokenId)).ShouldBeFalse();
        // The denied attempt must leave a forensic trail — this is the control the brief hinges on.
        (await HasAuditAsync(seeded.StatementId, AuditAction.DownloadDenied)).ShouldBeTrue();
    }

    [Fact]
    public async Task IpBoundToken_Should_Succeed_FromMatchingIp_AndAuditDownloaded()
    {
        HttpClient client = _factory.CreateClient();
        SeededToken seeded = await SeedAsync(ipAddress: "198.51.100.7");

        using var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl(seeded.Token));
        request.Headers.Add(StatementDeliveryWebApplicationFactory.TestClientIpHeader, "198.51.100.7");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TokenIsUsedAsync(seeded.TokenId)).ShouldBeTrue();
        (await HasAuditAsync(seeded.StatementId, AuditAction.StatementDownloaded)).ShouldBeTrue();
    }

    [Fact]
    public async Task SuccessfulDownload_Should_WriteDownloadedAuditRow()
    {
        HttpClient client = _factory.CreateClient();
        SeededToken seeded = await SeedAsync();

        HttpResponseMessage response = await client.GetAsync(DownloadUrl(seeded.Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await HasAuditAsync(seeded.StatementId, AuditAction.StatementDownloaded)).ShouldBeTrue();
    }

    private static Uri DownloadUrl(string token) =>
        new($"/statements/download?token={token}", UriKind.Relative);

    // Seeds a customer, a stored PDF, an active (or revoked) statement, and a matching single-use
    // download token straight through the domain + storage services the app itself uses.
    private async Task<SeededToken> SeedAsync(
        DateTime? expiresAt = null,
        string? ipAddress = null,
        bool revoked = false)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        IFileStorageService storage = sp.GetRequiredService<IFileStorageService>();
        IDownloadTokenService tokenService = sp.GetRequiredService<IDownloadTokenService>();
        ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();

        DateTime expiry = expiresAt ?? DateTime.UtcNow.AddMinutes(5);
        var customerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        User user = User.Create(customerId, $"{customerId:N}@example.com", "Test", "Customer", "8001015009087").Value;

        using var content = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 test statement"));
        StoredFile stored = await storage.StoreAsync(
            "statement.pdf", content, "application/pdf", $"statements/{customerId}", CancellationToken.None);

        Statement statement = Statement.Create(
            customerId, adminId, "statement.pdf", stored.StoragePath,
            "application/pdf", stored.FileSizeBytes, "2024-01", "test").Value;

        if (revoked)
        {
            statement.Revoke(adminId, "revoked for test");
        }

        (string token, Guid tokenId) = tokenService.GenerateToken(statement.Id, customerId, expiry);

        var downloadToken = DownloadToken.Create(
            tokenId, statement.Id, customerId, tokenService.HashToken(token),
            expiry, isSingleUse: true, ipAddress: ipAddress);

        db.Users.Add(user);
        db.Statements.Add(statement);
        db.DownloadTokens.Add(downloadToken);
        await db.SaveChangesAsync(CancellationToken.None);

        return new SeededToken(token, statement.Id, tokenId);
    }

    private async Task<bool> TokenIsUsedAsync(Guid tokenId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.DownloadTokens
            .AsNoTracking()
            .Where(t => t.Id == tokenId)
            .Select(t => t.IsUsed)
            .SingleAsync();
    }

    private async Task<bool> HasAuditAsync(Guid statementId, AuditAction action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.DownloadAuditLogs
            .AsNoTracking()
            .AnyAsync(a => a.StatementId == statementId && a.Action == action);
    }

    private sealed record SeededToken(string Token, Guid StatementId, Guid TokenId);
}
