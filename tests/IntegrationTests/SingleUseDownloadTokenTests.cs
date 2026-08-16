using System.Net;
using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Storage;
using Domain.DownloadTokens;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IntegrationTests;

// End-to-end proof of the headline guarantee: a single-use download link is redeemable exactly
// once, even though the endpoint is anonymous. The token is consumed by an atomic conditional
// UPDATE, so the second redemption is rejected.
public sealed class SingleUseDownloadTokenTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task DownloadToken_Should_BeRedeemable_OnlyOnce()
    {
        HttpClient client = _factory.CreateClient();
        string token = await SeedStatementWithTokenAsync();

        var url = new Uri($"/statements/download?token={token}", UriKind.Relative);

        HttpResponseMessage first = await client.GetAsync(url);
        HttpResponseMessage second = await client.GetAsync(url);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        ((int)second.StatusCode).ShouldBeInRange(400, 499);
    }

    private async Task<string> SeedStatementWithTokenAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        IFileStorageService storage = sp.GetRequiredService<IFileStorageService>();
        IDownloadTokenService tokenService = sp.GetRequiredService<IDownloadTokenService>();
        ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();

        var customerId = Guid.NewGuid();
        var user = User.Create(customerId, "customer@example.com", "Test", "Customer");

        using var content = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 test statement"));
        StoredFile stored = await storage.StoreAsync(
            "statement.pdf", content, "application/pdf", $"statements/{customerId}", CancellationToken.None);

        Statement statement = Statement.Create(
            customerId, Guid.NewGuid(), "statement.pdf", stored.StoragePath,
            "application/pdf", stored.FileSizeBytes, "2024-01", "test").Value;

        (string token, Guid tokenId) = tokenService.GenerateToken(
            statement.Id, customerId, DateTime.UtcNow.AddMinutes(5));

        var downloadToken = DownloadToken.Create(
            tokenId, statement.Id, customerId, tokenService.HashToken(token),
            DateTime.UtcNow.AddMinutes(5), isSingleUse: true, ipAddress: null);

        db.Users.Add(user);
        db.Statements.Add(statement);
        db.DownloadTokens.Add(downloadToken);
        await db.SaveChangesAsync(CancellationToken.None);

        return token;
    }
}
